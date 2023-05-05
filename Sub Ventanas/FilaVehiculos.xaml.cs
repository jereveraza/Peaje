using Entidades;
using Entidades.Comunicacion;
using Entidades.ComunicacionAntena;
using Entidades.Logica;
using ModuloPantallaTeclado.Interfaces;
using Newtonsoft.Json;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    /// <summary>
    /// Lógica de interacción para FilaVehiculos.xaml
    /// </summary>
    public partial class VentanaFilaVehiculos : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private static VentanaFilaVehiculos _helper;
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        private FilaVehiculos _filaVehiculos;
        public bool TieneFoco { get; set; }
        #endregion

        #region Constructor de la clase
        public VentanaFilaVehiculos(IPantalla pantalla)
        {
            InitializeComponent();
            _pantalla = pantalla;
            _helper = this;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            TieneFoco = true;
            Clases.Utiles.TraducirControles<TextBlock>(borderFilaVehiculos);
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
            FrameworkElement control = (FrameworkElement)borderFilaVehiculos.Child;
            borderFilaVehiculos.Child = null;
            Close();
            return null;
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
                if (comandoJson.CodigoStatus == enmStatus.Ok && comandoJson.Accion == enmAccion.FILAVEHICULOS)
                {
                    _filaVehiculos = Utiles.ClassUtiles.ExtraerObjetoJson<FilaVehiculos>(comandoJson.Operacion);

                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        dataGridFilaVehiculos.ItemsSource = _filaVehiculos.fila;
                        dataGridFilaVehiculos.Items.Refresh();
                        if (_filaVehiculos.ListaTagsLeidos.Count == 0)
                            txtLstTags.Text = string.Empty;
                        else
                        {
                            foreach (Tag it in _filaVehiculos.ListaTagsLeidos)
                            {
                                txtLstTags.Text += it.NumeroTag?.Trim() + "-";
                            }
                        }
                    }));
                }
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
                _logger.Debug("FilaVehiculos:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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

        }
        #endregion

        public static void CerrarVentanaFilaVehiculos()
        {
            if (_helper != null && _helper.IsLoaded)
                _helper.Close();
        }
    }
}
