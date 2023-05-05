using Entidades;
using Entidades.Comunicacion;
using Entidades.ComunicacionBaseDatos;
using Entidades.Logica;
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
    public partial class VentanaCategorias : Window , ISubVentana
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
        #endregion

        #region Constructor de la clase
        public VentanaCategorias(IPantalla padre)
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
                
                //Si me llego la patente de logica, la muestro el textbox
                if (_pantalla.ParametroAuxiliar != string.Empty)
                {
                    CargarDatosEnControles(_pantalla.ParametroAuxiliar);
                    _operador = Utiles.ClassUtiles.ExtraerObjetoJson<Operador>(_pantalla.ParametroAuxiliar);
                }
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
            FrameworkElement control = (FrameworkElement)borderVentanaCategorias.Child;
            borderVentanaCategorias.Child = null;
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

            }
            else if (Teclado.IsConfirmationKey(tecla))
            {

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

            }
            else if (Teclado.IsConfirmationKey(tecla))
            {

            }

            List<DatoVia> listaDV = new List<DatoVia>();

            //Espero el ingreso de patente del usuario
            if (Teclado.IsConfirmationKey(tecla))
            {
                SetTextoBotonesAceptarCancelar("Enter", "Escape");
                //Envio la consulta a logica y seteo el nuevo estado
                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    EnviarDatosALogica(enmStatus.Ok, enmAccion.T_ENTER, Newtonsoft.Json.JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                }));
            }
            else if (Teclado.IsEscapeKey(tecla))
            {
                _pantalla.CargarSubVentana(enmSubVentana.Categorias);
            }
            else
            {
                _pantalla.CargarSubVentana(enmSubVentana.Principal);
                _pantalla.AnalizaTecla(tecla);
            }
        }
        #endregion

        #region Procesamiento de pantalla tactil
        private void AUTO_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.D1);
        }

        private void P02_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.D2);
        }

        private void P03_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.D3);
        }

        private void P04_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.D4);
        }

        private void P05_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.D5);
        }

        private void P06_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.D6);
        }

        private void P07_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.D7);
        }

        private void P08_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.D8);
        }

        private void P09_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.D9);
        }

        private void P10_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.D0);
        }

        private void FOTO_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.P);
        }

        private void PLACA_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.Z);
        }

        private void SIP_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.S);
        }

        private void TURNO_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.T);
        }

        private void MENU_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.M);
        }

        private void SUBE_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.L);
        }

        private void BAJA_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.E);
        }

        private void RETIRO_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.J);
        }

        private void EX1_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.F1);
        }

        private void EX2_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.F2);
        }

        private void EX3_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.F3);
        }

        private void SEM_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.F);
        }

        private void VENTA_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaToque(Key.V);
        }

        private void VUELTO_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            _pantalla.AnalizaTecla(Key.F5);
        }

        #endregion
    }
}
