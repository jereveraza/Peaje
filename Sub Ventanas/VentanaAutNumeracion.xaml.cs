using Entidades;
using Entidades.Comunicacion;
using Entidades.ComunicacionBaseDatos;
using ModuloPantallaTeclado.Clases;
using ModuloPantallaTeclado.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    /// <summary>
    /// Lógica de interacción para VentanaAutNumeracion.xaml
    /// </summary>
    public partial class VentanaAutNumeracion : Window , ISubVentana
    {
        
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private Numeracion _numeracion = null;
        private Operador _operador = null;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgConfirmaNumeracion = "Confirme la numeración";
        const string msgConsultaNumEst = "Numeración de la estación";
        const string msgConsultaNumLocal = "Numeración almacenada localmente";
        #endregion

        #region Constructor de la clase
        public VentanaAutNumeracion(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            _numeracion = Utiles.ClassUtiles.ExtraerObjetoJson<Numeracion>(_pantalla.ParametroAuxiliar);
            SetTextoBotonesAceptarCancelar("Enter", "Escape");
            Clases.Utiles.TraducirControles<TextBlock>(Grid_Principal);
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                _pantalla.MensajeDescripcion(msgConfirmaNumeracion);
                
                //Si me llego la patente de logica, la muestro el textbox
                if (_pantalla.ParametroAuxiliar != string.Empty)
                {
                    CargarDatosEnControles(_pantalla.ParametroAuxiliar);
                    if (_numeracion.Consulta)
                    {
                        if(_numeracion.OrigenDatos == "ESTACION")
                            _pantalla.MensajeDescripcion(msgConsultaNumEst);
                        else
                            _pantalla.MensajeDescripcion(msgConsultaNumLocal);
                    }
                    else
                        _pantalla.MensajeDescripcion(msgConfirmaNumeracion);
                    _operador = Utiles.ClassUtiles.ExtraerObjetoJson<Operador>(_pantalla.ParametroAuxiliar);
                }
            }));
        }
        #endregion

        public void SetTextoBotonesAceptarCancelar(string TeclaConfirmacion, string TeclaCancelacion, bool BtnAceptarSiguiente = false)
        {
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                if(_numeracion.Consulta && _numeracion.OrigenDatos == "BASE DE DATOS LOCAL")
                    btnAceptar.Content = Traduccion.Traducir("Actualizar") + " [" + Teclado.GetEtiquetaTecla(TeclaConfirmacion) + "]";
                else
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
            FrameworkElement control = (FrameworkElement)borderVentanaAutNumeracion.Child;
            borderVentanaAutNumeracion.Child = null;
            Close();
            return control;
        }
        #endregion

        #region Metodo de carga de textboxes de datos
        private void CargarDatosEnControles(string datos)
        {
            try
            {
                //_numeracion = Utiles.ClassUtiles.ExtraerObjetoJson<Numeracion>(datos);

                if (_numeracion != null)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        txtBoxUltimoBloque.Text = _numeracion.NumeroTurno.ToString();
                        txtBoxUltimoTransito.Text = _numeracion.NumeroTransito.ToString();
                        txtBoxUltimoTicket.Text = _numeracion.Boleta;
                        txtBoxOrigenDatos.Text = _numeracion.OrigenDatos;
                        txtBoxFactura.Text = _numeracion.Factura;
                        txtBoxDetraccion.Text = _numeracion.Detraccion.ToString();
                    }));
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaAutNumeracion:CargarDatosEnControles() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaAutNumeracion:CargarDatosEnControles() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
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
                if (comandoJson.Accion == enmAccion.ESTADO_SUB &&
                        (comandoJson.CodigoStatus == enmStatus.FallaCritica || comandoJson.CodigoStatus == enmStatus.Ok))
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }));
                }
                /*else if (comandoJson.CodigoStatus == enmStatus.Error)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        txtPassword.Password = string.Empty;
                        txtPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyleHighlighted);
                    }));
                    _enmIngresoUsuario = enmIngresoUsuario.Password;
                }
                else*/
                bRet = true;
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaAutNumeracion:RecibirDatosLogica() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaAutNumeracion:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
                _logger.Debug("VentanaAutNumeracion:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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

            //Espero el ingreso de patente del usuario
            if (Teclado.IsConfirmationKey(tecla))
            {
                SetTextoBotonesAceptarCancelar("Enter", "Escape");
                //Envio la consulta a logica y seteo el nuevo estado
                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    Utiles.ClassUtiles.InsertarDatoVia(_numeracion, ref listaDV);
                    Utiles.ClassUtiles.InsertarDatoVia(_operador, ref listaDV);

                    EnviarDatosALogica(enmStatus.Ok, enmAccion.NUMERACION, Newtonsoft.Json.JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));

                    if (!_numeracion.Consulta)
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                }));
            }
            else if (Teclado.IsEscapeKey(tecla))
            {
                if(!_numeracion.Consulta)
                    EnviarDatosALogica(enmStatus.Abortada, enmAccion.NUMERACION, string.Empty);
                _pantalla.CargarSubVentana(enmSubVentana.Principal);
            }
        }
        #endregion

        private void ENTER_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(System.Windows.Input.Key.Enter);
        }

        private void ESC_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(System.Windows.Input.Key.Escape);
        }
    }
}
