using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Entidades;
using ModuloPantallaTeclado.Clases;
using Newtonsoft.Json;
using Entidades.Comunicacion;
using Entidades;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Controls;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmTicketManual { IngresoNroTicket, IngresoPtoVenta, ConfirmoDatos }

    /// <summary>
    /// Lógica de interacción para VentanaTicketManual.xaml
    /// </summary>
    public partial class VentanaTicketManual : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmTicketManual _enmTicketManual;
        private string _nroTicketManual, _nroPtoVenta;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgIngresoTicket = "Ingrese el numero de ticket manual y presione {0} para confirmar, {1} para volver.";
        const string msgIngresoPtoVenta = "Ingrese el punto de venta y presione {0}, {1} para volver.";
        const string msgConfirmeOperacion = "Presione {0} para confirmar los datos, {1} para volver.";
        #endregion

        #region Constructor de la clase
        /// <summary>
        /// Constructor de la clase
        /// </summary>
        /// <param name="padre"></param>
        public VentanaTicketManual(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            Clases.Utiles.TraducirControles<TextBlock>(Grid_Principal);
            _enmTicketManual = enmTicketManual.IngresoNroTicket;
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgIngresoTicket),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
            }));
        }
        #endregion

        public void SetTextoBotonesAceptarCancelar(string TeclaConfirmacion, string TeclaCancelacion, bool BtnAceptarSiguiente = false)
        {
            
        }

        #region Metodo para obtener el control desde el padre
        /// <summary>
        /// Obtiene el control que se quiere agregar a la ventana principal
        /// <returns>FrameworkElement</returns>
        /// </summary>
        public FrameworkElement ObtenerControlPrincipal()
        {
            FrameworkElement control = (FrameworkElement)borderTicketManual.Child;
            borderTicketManual.Child = null;
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
            try
            {

            }
            catch
            {

            }
            return true;
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
                _logger.Debug("VentanaTicketManual:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar enviar datos a logica.");
            }
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
            if (Teclado.IsEscapeKey(tecla))
            {
                if (_enmTicketManual == enmTicketManual.IngresoNroTicket)
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        TicketManual tktmanual = new TicketManual(string.Empty, string.Empty);
                        Utiles.ClassUtiles.InsertarDatoVia(tktmanual, ref listaDV);
                        EnviarDatosALogica(enmStatus.Abortada, enmAccion.TICKETMANUAL, JsonConvert.SerializeObject(tktmanual, jsonSerializerSettings));
                    }));
                }
                else if(_enmTicketManual == enmTicketManual.IngresoPtoVenta)
                {
                    _enmTicketManual = enmTicketManual.IngresoNroTicket;
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgIngresoTicket),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                        txtBoxNroTicket.Text = string.Empty;
                        txtBoxPtoVenta.Text = string.Empty;
                    }));
                }
                else if (_enmTicketManual == enmTicketManual.ConfirmoDatos)
                {
                    _enmTicketManual = enmTicketManual.IngresoPtoVenta;
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgIngresoPtoVenta),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                        txtBoxPtoVenta.Text = string.Empty;
                    }));
                }
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                if (_enmTicketManual == enmTicketManual.IngresoNroTicket)
                {
                    _enmTicketManual = enmTicketManual.IngresoPtoVenta;
                    _nroTicketManual = txtBoxNroTicket.Text;
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgIngresoPtoVenta),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                    }));
                }
                else if (_enmTicketManual == enmTicketManual.IngresoPtoVenta)
                {
                    _enmTicketManual = enmTicketManual.ConfirmoDatos;
                    _nroPtoVenta = txtBoxPtoVenta.Text;
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgConfirmeOperacion),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                    }));
                }
                else if(_enmTicketManual == enmTicketManual.ConfirmoDatos)
                {                    
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        TicketManual tktmanual = new TicketManual(_nroTicketManual, _nroPtoVenta);
                        Utiles.ClassUtiles.InsertarDatoVia(tktmanual, ref listaDV);
                        EnviarDatosALogica(enmStatus.Ok, enmAccion.TICKETMANUAL, JsonConvert.SerializeObject(tktmanual));
                    }));
                }
            }
            else if (Teclado.IsBackspaceKey(tecla))
            {
                if (_enmTicketManual == enmTicketManual.IngresoNroTicket)
                {
                    if (txtBoxNroTicket.Text.Length > 0)
                        txtBoxNroTicket.Text = txtBoxNroTicket.Text.Remove(txtBoxNroTicket.Text.Length - 1);
                }
                else if (_enmTicketManual == enmTicketManual.IngresoPtoVenta)
                {
                    if (txtBoxPtoVenta.Text.Length > 0)
                        txtBoxPtoVenta.Text = txtBoxPtoVenta.Text.Remove(txtBoxPtoVenta.Text.Length - 1);
                }
            }
            else if(Teclado.IsNumericKey(tecla))
            {
                if(_enmTicketManual == enmTicketManual.IngresoNroTicket)
                {
                    txtBoxNroTicket.Text += Teclado.GetKeyAlphaNumericValue(tecla);
                }
                else if(_enmTicketManual == enmTicketManual.IngresoPtoVenta)
                {
                    txtBoxPtoVenta.Text += Teclado.GetKeyAlphaNumericValue(tecla);
                }
            }
        }
        #endregion
    }
}
