using Entidades;
using Entidades.Comunicacion;
using ModuloPantallaTeclado.Clases;
using ModuloPantallaTeclado.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmIngresoViaEstacion { Via, Estacion, Confirmar }

    /// <summary>
    /// Lógica de interacción para IngresoViaEstacion.xaml
    /// </summary>
    public partial class IngresoViaEstacion : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private int _maximoCaracteres;
        private enmIngresoViaEstacion _enmIngresoViaEstacion;
        private IPantalla _pantalla = null;
        private NameValueCollection _sectorTeclas;
        private Causa _causaRecibida;
        private bool _confirmarIngreso = true;
        public string ParametroAuxiliar { set; get; }
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgIngresoVia = "Ingrese número de vía";
        const string msgIngresoEstacion = "Ingrese número de estación";
        const string msgConfirmaOperacion = "Confirme la operación";
        const string msgDeseaConfirmar = "Está seguro que desea confirmar?";
        #endregion

        #region Constructor de la clase
        /// <summary>
        /// Constructor de la clase
        /// </summary>
        /// <param name="padre"></param>
        public IngresoViaEstacion(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        public void SetTextoBotonesAceptarCancelar(string TeclaConfirmacion, string TeclaCancelacion, bool BtnAceptarSiguiente = false)
        {
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                btnAceptar.Content = Traduccion.Traducir(BtnAceptarSiguiente ? "Siguiente" : "Confirmar") + " [" + Teclado.GetEtiquetaTecla(TeclaConfirmacion) + "]";
                btnCancelar.Content = Traduccion.Traducir("Volver") + " [" + Teclado.GetEtiquetaTecla(TeclaCancelacion) + "]";
            }));
        }

        #region Inicializacion de la ventana
        private void gridIngresoViaEstacion_Loaded(object sender, RoutedEventArgs e)
        {
            _sectorTeclas = (NameValueCollection)ConfigurationManager.GetSection("caracteres");
            //_maximoCaracteres = Convert.ToInt32(_sectorTeclas["maximoCaracteres"]);
            _maximoCaracteres = 3;
            _enmIngresoViaEstacion = enmIngresoViaEstacion.Via;

            SetTextoBotonesAceptarCancelar("Enter", "Escape", true);

            txtVia.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
            txtEstacion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
            txtEstacion.VerticalContentAlignment = VerticalAlignment.Center;
            txtEstacion.MaxLength = _maximoCaracteres;
            
            _pantalla.MensajeDescripcion(msgIngresoVia);
            Clases.Utiles.TraducirControles<TextBlock>(gridIngresoViaEstacion);

            try
            {
                _causaRecibida = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(_pantalla.ParametroAuxiliar);
                Opcion opcionConfirmacion = Utiles.ClassUtiles.ExtraerObjetoJson<Opcion>(_pantalla.ParametroAuxiliar);

                _confirmarIngreso = opcionConfirmacion.Confirmar;

                lblIngresoViaEstacion.Dispatcher.Invoke((Action)(() => lblIngresoViaEstacion.Text = _causaRecibida.Descripcion));
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("IngresoViaEstacion:gridIngresoViaEstacion_Loaded() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("IngresoViaEstacion:gridIngresoViaEstacion_Loaded() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

        #region Metodo para obtener el control desde el padre
        /// <summary>
        /// Obtiene el control que se quiere agregar a la ventana principal
        /// <returns>FrameworkElement</returns>
        /// </summary>
        public FrameworkElement ObtenerControlPrincipal()
        {
            FrameworkElement control = (FrameworkElement)borderIngresoViaEstacion.Child;
            borderIngresoViaEstacion.Child = null;
            Close();
            return control;
        }
        #endregion

        #region Metodos de comunicacion con el modulo de logica
        /// <summary>
        /// Este metodo recibe el string JSON que llega desde el socket
        /// </summary>
        /// <param name="comandoJson"></param>
        public bool RecibirDatosLogica(ComandoLogica comandoJson)
        {
            bool bRet = false;
            try
            {
                if ( comandoJson.CodigoStatus == enmStatus.Ok 
                    && comandoJson.Accion == enmAccion.ESTADO )
                {
                    Causa causa = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(comandoJson.Operacion);

                    if (causa.Codigo == eCausas.AperturaTurno
                        || causa.Codigo == eCausas.CausaCierre)
                    {
                        //Logica indica que se debe cerrar la ventana
                        Application.Current.Dispatcher.Invoke((Action)(() =>
                        {
                            _pantalla.MensajeDescripcion(string.Empty);
                            _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        }));
                    }
                }
                else if (comandoJson.CodigoStatus == enmStatus.Error)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        txtVia.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                    }));

                    _enmIngresoViaEstacion = enmIngresoViaEstacion.Via;
                }
                else if (comandoJson.Accion == enmAccion.ESTADO_SUB &&
                        (comandoJson.CodigoStatus == enmStatus.FallaCritica || comandoJson.CodigoStatus == enmStatus.Ok))
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }));
                }
                else
                    bRet = true;
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("IngresoViaEstacion:RecibirDatosLogica() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("IngresoViaEstacion:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            return bRet;
        }

        /// <summary>
        /// Este metodo envia un json formateado en string hacia el socket
        /// </summary>
        /// <param name="status"></param>
        /// <param name="Accion"></param>
        /// <param name="Operacion"></param>
        public void EnviarDatosALogica(enmStatus status, enmAccion Accion, string Operacion)
        {
            try
            {
                _pantalla.EnviarDatosALogica(status, Accion, Operacion);
            }
            catch (Exception ex)
            {
                _logger.Debug("IngresoViaEstacion:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar enviar datos a logica.");
            }
        }
        #endregion

        #region Metodo de procesamiento de tecla recibida
        public void ProcesarTeclaUp(Key tecla)
        {
            if (Teclado.IsEscapeKey(tecla))
            {
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    btnCancelar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyle);
                }));
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyle);
                }));
            }
        }

        /// <summary>
        /// A este metodo llegan las teclas recibidas desde la pantalla principal
        /// </summary>
        /// <param name="tecla"></param>
        public void ProcesarTecla(Key tecla)
        {
            //Aplico el estilo al presionar
            if (Teclado.IsEscapeKey(tecla))
            {
                btnCancelar.Dispatcher.Invoke((Action)(() =>
                {
                    btnCancelar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyleHighlighted);
                }));
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                btnAceptar.Dispatcher.Invoke((Action)(() =>
                {
                    btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyleHighlighted);
                }));
            }

            List<DatoVia> listaDV = new List<DatoVia>();
            if (Teclado.IsBackspaceKey(tecla))
            {
                if (_enmIngresoViaEstacion == enmIngresoViaEstacion.Via)
                {
                    if (txtVia.Text.Length > 0)
                        txtVia.Text = txtVia.Text.Remove(txtVia.Text.Length - 1);

                    txtVia.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyle );
                }

                else if (_enmIngresoViaEstacion == enmIngresoViaEstacion.Estacion)
                {
                    if (txtEstacion.Text.Length > 0)
                        txtEstacion.Text = txtEstacion.Text.Remove(txtEstacion.Text.Length - 1);

                    txtEstacion.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyle );
                }
            }
            else  if (Teclado.IsConfirmationKey(tecla))
            {
                if (_enmIngresoViaEstacion == enmIngresoViaEstacion.Confirmar)
                {
                    _enmIngresoViaEstacion = enmIngresoViaEstacion.Via;

                    SetTextoBotonesAceptarCancelar("Enter", "Escape");

                    var viaEstacion = new ViaEstacion( txtVia.Text, txtEstacion.Text );

                    Utiles.ClassUtiles.InsertarDatoVia( viaEstacion, ref listaDV);
                    Utiles.ClassUtiles.InsertarDatoVia(_causaRecibida, ref listaDV);

                    EnviarDatosALogica(enmStatus.Ok, enmAccion.VIAESTACION, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    txtVia.Text = string.Empty;
                    txtEstacion.Text = string.Empty;
                    txtVia.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                    txtEstacion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                    _pantalla.MensajeDescripcion(string.Empty);
                    //_pantalla.CargarSubVentana(enmSubVentana.Principal);
                }

                if (_enmIngresoViaEstacion == enmIngresoViaEstacion.Via)
                {
                    if (txtVia.Text != string.Empty)
                    {
                        SetTextoBotonesAceptarCancelar("Enter", "Escape", true);
                        _enmIngresoViaEstacion = enmIngresoViaEstacion.Estacion;
                        txtVia.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                        txtEstacion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                        _pantalla.MensajeDescripcion(msgIngresoEstacion);
                    }
                }

                else if (_enmIngresoViaEstacion == enmIngresoViaEstacion.Estacion)
                {
                    if(_confirmarIngreso)
                    {
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        _enmIngresoViaEstacion = enmIngresoViaEstacion.Confirmar;
                        _pantalla.MensajeDescripcion(msgConfirmaOperacion);
                    }
                    else
                    {
                        _enmIngresoViaEstacion = enmIngresoViaEstacion.Via;

                        var viaEstacion = new ViaEstacion(txtVia.Text, txtEstacion.Text);
                        

                        txtVia.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                        txtEstacion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle );

                        Utiles.ClassUtiles.InsertarDatoVia(viaEstacion, ref listaDV);
                        Utiles.ClassUtiles.InsertarDatoVia(_causaRecibida, ref listaDV);

                        EnviarDatosALogica(enmStatus.Ok, enmAccion.VIAESTACION, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        txtVia.Text = string.Empty;
                        txtEstacion.Text = string.Empty;
                        _pantalla.MensajeDescripcion(string.Empty);
                        //_pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                }
            }
            else if (Teclado.IsEscapeKey(tecla))
            {
                if (_enmIngresoViaEstacion == enmIngresoViaEstacion.Confirmar)
                {
                    SetTextoBotonesAceptarCancelar("Enter", "Escape", true);
                    _enmIngresoViaEstacion = enmIngresoViaEstacion.Estacion;
                    txtVia.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                    txtEstacion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted );
                    _pantalla.MensajeDescripcion(msgIngresoEstacion);
                }
                else if (_enmIngresoViaEstacion == enmIngresoViaEstacion.Estacion)
                {
                    txtEstacion.Text = string.Empty;
                    _enmIngresoViaEstacion = enmIngresoViaEstacion.Via;
                    txtVia.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                    txtEstacion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle );
                    _pantalla.MensajeDescripcion(msgIngresoVia);
                }
                else if (_enmIngresoViaEstacion == enmIngresoViaEstacion.Via)
                {
                    txtVia.Text = string.Empty;
                    txtEstacion.Text = string.Empty;
                    txtVia.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                    txtEstacion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);

                    var viaEstacion = new ViaEstacion(string.Empty, string.Empty);
                    Utiles.ClassUtiles.InsertarDatoVia(viaEstacion, ref listaDV);

                    EnviarDatosALogica(enmStatus.Abortada, enmAccion.VIAESTACION, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));

                    _pantalla.MensajeDescripcion(string.Empty);
                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                }
            }
            else
            {
                if (_enmIngresoViaEstacion == enmIngresoViaEstacion.Via)
                {
                    if (txtVia.Text.Length < _maximoCaracteres)
                    {
                        if (Teclado.IsNumericKey(tecla))
                            txtVia.Text += Teclado.GetKeyAlphaNumericValue(tecla);
                    }
                    else
                    {
                        txtVia.Background = new SolidColorBrush(Color.FromRgb(255, 0, 0));
                    }
                }
                else if (_enmIngresoViaEstacion == enmIngresoViaEstacion.Estacion)
                {
                    if (txtEstacion.Text.Length < _maximoCaracteres)
                    {
                        if (Teclado.IsNumericKey(tecla))
                            txtEstacion.Text += Teclado.GetKeyAlphaNumericValue(tecla);
                    }
                    else
                    {
                        txtEstacion.Background = new SolidColorBrush( Color.FromRgb( 255, 0, 0 ) );
                    }
                }
            }
        }
        #endregion
    }
}
