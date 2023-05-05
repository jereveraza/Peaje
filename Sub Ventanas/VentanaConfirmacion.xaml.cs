using ModuloPantallaTeclado.Interfaces;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Entidades;
using Entidades.Comunicacion;
using Newtonsoft.Json;
using ModuloPantallaTeclado.Clases;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    /// <summary>
    /// Lógica de interacción para VentanaConfirmacion.xaml
    /// </summary>
    public partial class VentanaConfirmacion : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private Causa _causa = null;
        private bool _permiteCancelarConfirmacion;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgConfirmar = "Confirme con {0}, {1} para volver.";
        const string msgSoloConfirmar = "Confirme con {0}.";
        #endregion

        #region Constructor de la clase
        public VentanaConfirmacion(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (_pantalla.ParametroAuxiliar != string.Empty)
                {
                    _causa = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(_pantalla.ParametroAuxiliar);
                    CargarDatosEnControles(_pantalla.ParametroAuxiliar);
                }
            }));
        }
        #endregion

        public void SetTextoBotonesAceptarCancelar(string TeclaConfirmacion, string TeclaCancelacion, bool BtnAceptarSiguiente = false)
        {
            
        }

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

                    if (_causa.Codigo != eCausas.ReincioViaFaltaLlave
                        && (causa.Codigo == eCausas.AperturaTurno
                        || causa.Codigo == eCausas.CausaCierre
                        || causa.Codigo == eCausas.Salidavehiculo))
                    {
                        //Logica indica que se debe cerrar la ventana
                        Application.Current.Dispatcher.Invoke((Action)(() =>
                        {
                            _pantalla.MensajeDescripcion(string.Empty);
                            _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        }));
                    }
                }
                else
                    bRet = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaConfirmacion:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al recibir una Respuesta de logica.");
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
                _logger.Debug("VentanaConfirmacion:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar enviar datos a logica.");
            }
        }
        #endregion

        #region Metodo de carga de textboxes de datos
        private void CargarDatosEnControles(string datos)
        {
            try
            {
                if(_causa.Codigo == eCausas.ReincioViaFaltaLlave)
                {
                    _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgSoloConfirmar),
                                Teclado.GetEtiquetaTecla("Enter")),
                                false
                                );
                    _permiteCancelarConfirmacion = false;
                }
                else
                {
                    _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgConfirmar),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                    _permiteCancelarConfirmacion = true;
                }
                
                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    textBoxMsgeConfirmacion.Text = _causa.Descripcion;
                }));
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaConfirmacion:CargarDatosEnControles() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaConfirmacion:CargarDatosEnControles() Exception: {0}", ex.Message.ToString());
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
            FrameworkElement control = (FrameworkElement)borderVentanaConfirmacion.Child;
            borderVentanaConfirmacion.Child = null;
            Close();
            return control;
        }
        #endregion

        #region Metodo de procesamiento de tecla recibida
        public void ProcesarTeclaUp(Key tecla)
        {
            
        }

        /// <summary>
        /// A este metodo llegan las teclas recibidas desde la pantalla principal
        /// </summary>
        /// <param name="tecla"></param>
        public void ProcesarTecla(Key tecla)
        {
            List<DatoVia> listaDV = new List<DatoVia>();
            if (_permiteCancelarConfirmacion && Teclado.IsEscapeKey(tecla))
            {
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    _pantalla.MensajeDescripcion(string.Empty);
                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                }));
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                Utiles.ClassUtiles.InsertarDatoVia(_causa, ref listaDV);
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    EnviarDatosALogica(enmStatus.Ok, enmAccion.CONFIRMAR, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    _pantalla.MensajeDescripcion(string.Empty);
                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                }));
            }
        }
        #endregion

    }
}
