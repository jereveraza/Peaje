using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Clases;
using Entidades.Comunicacion;
using Entidades;
using Entidades.Logica;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Windows.Input;
using System.Windows.Controls;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmValePrepago { IngresoVale, Confirmacion }

    /// <summary>
    /// Lógica de interacción para VentanaValePrepago.xaml
    /// </summary>
    public partial class VentanaValePrepago : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmValePrepago _enmValePrepago;
        private Causa _causaRecibida;
        private InfoClearing _infoClearing;
        private int _longitudCodigo = 0;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgIngresoNroVale = "Ingrese el código y confirme con {0}, {1} para volver.";
        const string msgConfirme = "Confirme el código con {0}, {1} para volver.";
        const string msgMaximoNcaracteres = "Máximo {0} digitos";
        #endregion

        #region Constructor de la clase
        /// <summary>
        /// Constructor de la clase
        /// </summary>
        /// <param name="padre"></param>
        public VentanaValePrepago(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            _enmValePrepago = enmValePrepago.IngresoVale;
            SetTextoBotonesAceptarCancelar("Enter", "Escape");
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                txtCodigoBarras.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                _pantalla.MensajeDescripcion(
                                 string.Format(Traduccion.Traducir(msgIngresoNroVale),
                                 Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                 false
                                 );
            }));
            try
            {
                _causaRecibida = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(_pantalla.ParametroAuxiliar);
                lblTituloValePrepago.Dispatcher.Invoke((Action)(() => lblTituloValePrepago.Text = _causaRecibida.Descripcion));

                _infoClearing = Utiles.ClassUtiles.ExtraerObjetoJson<InfoClearing>(_pantalla.ParametroAuxiliar);
                _longitudCodigo = _infoClearing.CantCaracteres;
                if(!string.IsNullOrEmpty(_infoClearing.CodigoBarras))
                    txtCodigoBarras.Dispatcher.Invoke((Action)(() => txtCodigoBarras.Text = _infoClearing.CodigoBarras));
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("ValePrepago:Grid_Loaded() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("ValePrepago:Grid_Loaded() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            Clases.Utiles.TraducirControles<TextBlock>(Grid_Principal);
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

        #region Metodo para obtener el control desde el padre
        /// <summary>
        /// Obtiene el control que se quiere agregar a la ventana principal
        /// <returns>FrameworkElement</returns>
        /// </summary>
        public FrameworkElement ObtenerControlPrincipal()
        {
            FrameworkElement control = (FrameworkElement)borderValePrepago.Child;
            borderValePrepago.Child = null;
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
                if (comandoJson.CodigoStatus == enmStatus.Ok
                    && comandoJson.Accion == enmAccion.ESTADO)
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
                _logger.Debug("ValePrepago:RecibirDatosLogica() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("ValePrepago:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
                _logger.Debug("ValePrepago:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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
            if (Teclado.IsEscapeKey(tecla))
            {
                if (_enmValePrepago == enmValePrepago.IngresoVale)
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        txtCodigoBarras.Text = string.Empty;
                        EnviarDatosALogica(enmStatus.Abortada, enmAccion.VALE_PREPAGO, string.Empty);
                    }));
                }
                else if (_enmValePrepago == enmValePrepago.IngresoVale)
                {
                    _enmValePrepago = enmValePrepago.Confirmacion;
                    txtCodigoBarras.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                    _pantalla.MensajeDescripcion(
                                 string.Format(Traduccion.Traducir(msgIngresoNroVale),
                                 Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                 false
                                 );
                }
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                if (_enmValePrepago == enmValePrepago.IngresoVale)
                {
                    _enmValePrepago = enmValePrepago.Confirmacion;
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(
                                 string.Format(Traduccion.Traducir(msgConfirme),
                                 Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                 false
                                 );
                        txtCodigoBarras.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                    }));
                }
                else if (_enmValePrepago == enmValePrepago.Confirmacion)
                {
                    //validar los datos ingresados aca                    
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        _infoClearing.CodigoBarras = txtCodigoBarras.Text;
                        Utiles.ClassUtiles.InsertarDatoVia(_infoClearing, ref listaDV);
                        EnviarDatosALogica(enmStatus.Ok, enmAccion.AUTORIZACIONPASO, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    }));
                }
            }
            else if (Teclado.IsBackspaceKey(tecla))
            {
                if (_enmValePrepago == enmValePrepago.IngresoVale)
                {
                    if (txtCodigoBarras.Text.Length > 0)
                    {
                        if (txtCodigoBarras.Text.Length == _longitudCodigo)
                        {
                            _pantalla.MensajeDescripcion(
                                 string.Format(Traduccion.Traducir(msgIngresoNroVale),
                                 Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                 false
                                 );
                        }
                        txtCodigoBarras.Text = txtCodigoBarras.Text.Remove(txtCodigoBarras.Text.Length - 1);
                    }
                }
            }
            else if (Teclado.IsNumericKey(tecla))
            {
                if (_enmValePrepago == enmValePrepago.IngresoVale)
                {
                    if (txtCodigoBarras.Text.Length < _longitudCodigo)
                    {
                        txtCodigoBarras.Text += Teclado.GetKeyNumericValue(tecla);
                        _pantalla.MensajeDescripcion(
                                 string.Format(Traduccion.Traducir(msgIngresoNroVale),
                                 Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                 false
                                 );
                    }
                    else
                        _pantalla.MensajeDescripcion(string.Format(Traduccion.Traducir(msgMaximoNcaracteres),_longitudCodigo),false);
                }
            }
        }
        #endregion
    }
}
