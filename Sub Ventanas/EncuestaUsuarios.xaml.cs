using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Clases;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Windows.Controls;
using Entidades.Comunicacion;
using Entidades;
using Entidades.Logica;
using System.Windows.Input;
using Utiles.Utiles;
using System.Linq;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmEncuesta { SeleccionOpcion, ObtenerDatos, ConfirmoOpcion }

    /// <summary>
    /// Lógica de interacción para VentanaCobroDeudas.xaml
    /// </summary>
    public partial class VentanaEncuestaUsuarios : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmEncuesta _enmEncuesta;
        private Encuesta _listaEncuesta;
        private List<ListadoRespuestas> _respuestasElegidas;
        private int _cantidadPreguntas = 0;
        private int _itemSeleccionado = 0;
        private int index = 0;
        private int _paginaVisible = 0;
        private Vehiculo _vehiculo;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        private class DeudaConIndex
        {
            public DeudaConIndex(InfoDeuda deuda, int num, int pag)
            {
                NumeroDeuda = num;
                InfoDeuda = deuda;
                Pagina = pag;
                DeudaSeleccionada = false;
            }

            public bool DeudaSeleccionada { get; set; }

            public int NumeroDeuda { get; set; }

            public int Pagina { get; set; }

            public string IdPago { get { if (InfoDeuda.Id == 0) return "-"; else return InfoDeuda.Id.ToString(); } }

            public string Tipo
            {
                get
                {
                    if (InfoDeuda.Tipo == eTipoDeuda.PagoDiferido)
                        return Traduccion.Traducir("P DIF");
                    else if (InfoDeuda.Tipo == eTipoDeuda.Violacion)
                        return Traduccion.Traducir("VIOL");
                    else
                        return Traduccion.Traducir("TODAS");
                }
            }
            public string Estacion { get { if (InfoDeuda.Estacion == 0) return "-"; else return InfoDeuda.NombreEstacion; } }

            public string Monto { get { return Datos.FormatearMonedaAString(InfoDeuda.Monto); } }

            public string FechaHora { get { if (InfoDeuda.Estacion == 0) return "-"; else return InfoDeuda.FechaHora.ToString("dd-MM-yy   HH:mm"); } }

            public InfoDeuda InfoDeuda { get; set; }
        }
        #endregion

        #region Mensajes de descripcion
        const string msgSeleccionOpcionMultiplesPaginas = "Seleccione las deudas de la lista, {0} Siguiente pag.";
        const string msgSeleccioneRespuesta = "Seleccione la respuesta";
        const string msgDeseaConfirmarPago = "Confirme el pago de {0} con {1}, {2} para volver.";
        #endregion

        #region Defines
        private const int _filasVisiblesPrimerPaginaDeudas = 10;
        private int _RespuestaElegida = 0;
        #endregion

        #region Constructor de la clase
        /// <summary>
        /// Constructor de la clase
        /// </summary>
        /// <param name="padre"></param>
        public VentanaEncuestaUsuarios(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            _enmEncuesta = enmEncuesta.SeleccionOpcion;
            SetTextoBotonesAceptarCancelar("Enter", "Escape");
            Clases.Utiles.TraducirControles<TextBlock>(Grid_Principal);
            _respuestasElegidas = new List<ListadoRespuestas>();
            txtTituloVentana.Text = "Encuesta a Usuarios";
            lblNombreEncuesta.Text = "Pregunta";
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                txtOpcion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgSeleccioneRespuesta),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
            }));

            try
            {
                _vehiculo = Utiles.ClassUtiles.ExtraerObjetoJson<Vehiculo>(_pantalla.ParametroAuxiliar);

                _listaEncuesta = Utiles.ClassUtiles.ExtraerObjetoJson<Encuesta>(_pantalla.ParametroAuxiliar);
                _cantidadPreguntas = _listaEncuesta.ListaPreguntas.Count;
                if (_cantidadPreguntas != 0)
                {
                    CargarListaPreguntasEnControl(_listaEncuesta);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("CobroDeudas:Grid_Loaded() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

        public void SetTextoBotonesAceptarCancelar(string TeclaConfirmacion, string TeclaCancelacion, bool BtnAceptarSiguiente = false)
        {
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                btnAceptar.Content = Traduccion.Traducir("Confirmar") + " [" + Teclado.GetEtiquetaTecla(TeclaConfirmacion) + "]";
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
            FrameworkElement control = (FrameworkElement)borderEncuestaUsuarios.Child;
            borderEncuestaUsuarios.Child = null;
            Close();
            return control;
        }
        #endregion

        #region Metodos de carga de lista de deudas

        /// <summary>
        /// Verifica si opcion ingresada se encuentra dentro del rango de la pagina de clientes visible en pantalla
        /// </summary>
        /// <returns></returns>
        private bool EstaEnRangoDePaginaVisible(int num)
        {
            bool ret = false;
            return ret;
        }


        private void CargarListaPreguntasEnControl(Encuesta encuesta, int numeroPagina = 1)
        {
            try
            {
                if (encuesta.ListaPreguntas.Count > 0)
                {
                    _paginaVisible = numeroPagina;

                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        txtNombreEncuesta.Text = encuesta.ListaPreguntas[index].Descripcion.ToString();
                        if (encuesta.ListaPreguntas[index].ListaRespuestas.Count >= 1)
                        {
                            txtOpcion1.Text = "1: " + encuesta.ListaPreguntas[index].ListaRespuestas[0].Descripcion.ToString();
                            BotonOpcion1.Visibility = Visibility.Visible;
                            BotonOpcion1.Background = System.Windows.Media.Brushes.Black;
                        }
                        else
                            BotonOpcion1.Visibility = Visibility.Collapsed;

                        if (encuesta.ListaPreguntas[index].ListaRespuestas.Count >= 2)
                        {
                            txtOpcion2.Text = "2: " + encuesta.ListaPreguntas[index].ListaRespuestas[1].Descripcion.ToString();
                            BotonOpcion2.Visibility = Visibility.Visible;
                            BotonOpcion2.Background = System.Windows.Media.Brushes.Black;
                        }
                        else
                            BotonOpcion2.Visibility = Visibility.Collapsed;

                        if (encuesta.ListaPreguntas[index].ListaRespuestas.Count >= 3)
                        {
                            txtOpcion3.Text =  "3: " + encuesta.ListaPreguntas[index].ListaRespuestas[2].Descripcion.ToString();
                            BotonOpcion3.Visibility = Visibility.Visible;
                            BotonOpcion3.Background = System.Windows.Media.Brushes.Black;
                        }
                        else
                            BotonOpcion3.Visibility = Visibility.Collapsed;

                        if (encuesta.ListaPreguntas[index].ListaRespuestas.Count >= 4)
                        {
                            txtOpcion4.Text = "4: " + encuesta.ListaPreguntas[index].ListaRespuestas[3].Descripcion.ToString();
                            BotonOpcion4.Visibility = Visibility.Visible;
                            BotonOpcion4.Background = System.Windows.Media.Brushes.Black;
                        }
                        else
                            BotonOpcion4.Visibility = Visibility.Collapsed;

                        if (encuesta.ListaPreguntas[index].ListaRespuestas.Count >= 5)
                        {
                            txtOpcion5.Text = "5: " + encuesta.ListaPreguntas[index].ListaRespuestas[4].Descripcion.ToString();
                            BotonOpcion5.Visibility = Visibility.Visible;
                            BotonOpcion5.Background = System.Windows.Media.Brushes.Black;
                        }
                        else
                            BotonOpcion5.Visibility = Visibility.Collapsed;

                        if (encuesta.ListaPreguntas[index].ListaRespuestas.Count >= 6)
                        {
                            txtOpcion6.Text = "6: " + encuesta.ListaPreguntas[index].ListaRespuestas[5].Descripcion.ToString();
                            BotonOpcion6.Visibility = Visibility.Visible;
                            BotonOpcion6.Background = System.Windows.Media.Brushes.Black;
                        }
                        else
                            BotonOpcion6.Visibility = Visibility.Collapsed;
                    }));
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaCobroDeudas:CargarListaDeudasEnControl() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaCobroDeudas:CargarListaDeudasEnControl() Exception: {0}", ex.Message.ToString());
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
                if (comandoJson.CodigoStatus == enmStatus.Ok
                    && comandoJson.Accion == enmAccion.ESTADO)
                {
                    Causa causa = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(comandoJson.Operacion);

                    if (causa.Codigo == eCausas.AperturaTurno
                        || causa.Codigo == eCausas.CausaCierre
                        || causa.Codigo == eCausas.Salidavehiculo)
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
            catch (JsonException jsonEx)
            {
                _logger.Debug("CobroDeudas:RecibirDatosLogica() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("CobroDeudas:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
                _logger.Debug("CobroDeudas:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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
            else if (Teclado.IsConfirmationKey(tecla) || Teclado.IsCashKey(tecla))
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
            else if (Teclado.IsConfirmationKey(tecla) || ( Teclado.IsCashKey(tecla) || tecla == Key.C))
            {
                btnAceptar.Dispatcher.Invoke((Action)(() =>
                {
                    btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyleHighlighted);
                }));
            }

            List<DatoVia> listaDV = new List<DatoVia>();
            if (Teclado.IsEscapeKey(tecla))
            {
                if (_enmEncuesta == enmEncuesta.SeleccionOpcion)
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        index--;
                        if (index == 0)
                        {
                            _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        }
                        else
                        {
                            foreach (var tm in _listaEncuesta.ListaPreguntas[index].ListaRespuestas)
                            {
                                _listaEncuesta.ListaPreguntas[index].ListaRespuestas[tm.Codigo].Seleccionada = false;
                            }
                            CargarListaPreguntasEnControl(_listaEncuesta);
                        }                            

                    }));
                }
                else if (_enmEncuesta == enmEncuesta.ConfirmoOpcion)
                {
                    _enmEncuesta = enmEncuesta.SeleccionOpcion;
                    txtOpcion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                    SetTextoBotonesAceptarCancelar("Enter", "Escape");
                    _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgSeleccioneRespuesta),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                }
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                if (_enmEncuesta == enmEncuesta.SeleccionOpcion)
                {
                    txtOpcion.Dispatcher.BeginInvoke((Action)(() => txtOpcion.Text = string.Empty));
                    ListadoRespuestas respuesta = new ListadoRespuestas();
                    _listaEncuesta.ListaPreguntas[index].ListaRespuestas[_RespuestaElegida-1].Seleccionada = true;
                    index++;
                    if (index < _listaEncuesta.ListaPreguntas.Count)
                        CargarListaPreguntasEnControl(_listaEncuesta);
                    else
                    {
                        EnviarDatosALogica(enmStatus.Ok, enmAccion.T_ENCUESTA, JsonConvert.SerializeObject(_listaEncuesta, jsonSerializerSettings));
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                }
            }
            
            else if (Teclado.IsNumericKey(tecla))
            {
                int teclaNumerica = Teclado.GetKeyNumericValue(tecla);
                if (_enmEncuesta == enmEncuesta.SeleccionOpcion)
                {
                    switch(tecla)
                    {
                        case Key.D1:
                            if(!string.IsNullOrEmpty(txtOpcion1.Text))
                                Opcion1_Click();
                            break;
                        case Key.D2:
                            if (!string.IsNullOrEmpty(txtOpcion2.Text))
                                Opcion2_Click();
                            break;
                        case Key.D3:
                            if (!string.IsNullOrEmpty(txtOpcion3.Text))
                                Opcion3_Click();
                            break;
                        case Key.D4:
                            if (!string.IsNullOrEmpty(txtOpcion4.Text))
                                Opcion4_Click();
                            break;
                        case Key.D5:
                            if (!string.IsNullOrEmpty(txtOpcion5.Text))
                                Opcion5_Click();
                            break;
                        case Key.D6:
                            if (!string.IsNullOrEmpty(txtOpcion6.Text))
                                Opcion6_Click();
                            break;
                        default:
                            break;
                    }
                        
                }
            }
        }
        #endregion

        #region Pantalla Tactil

        private void ENTER_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(Key.Enter);
        }

        private void ESC_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(Key.Escape);
        }

        private void Salir_Click(object sender, RoutedEventArgs e)
        {
            EnviarDatosALogica(enmStatus.Abortada, enmAccion.T_ENCUESTA, string.Empty);
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
        }

        private void Opcion1_Click(object sender=null, RoutedEventArgs e=null)
        {
            _RespuestaElegida = 1;
            BotonOpcion1.Background = System.Windows.Media.Brushes.Gray;

            BotonOpcion2.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion3.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion4.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion5.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion6.Background = System.Windows.Media.Brushes.Black;
        }
        private void Opcion2_Click(object sender = null, RoutedEventArgs e = null)
        {
            _RespuestaElegida = 2;
            BotonOpcion2.Background = System.Windows.Media.Brushes.Gray;

            BotonOpcion1.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion3.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion4.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion5.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion6.Background = System.Windows.Media.Brushes.Black;
        }
        private void Opcion3_Click(object sender = null, RoutedEventArgs e = null)
        {
            _RespuestaElegida = 3;
            BotonOpcion3.Background = System.Windows.Media.Brushes.Gray;

            BotonOpcion1.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion2.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion4.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion5.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion6.Background = System.Windows.Media.Brushes.Black;
        }
        private void Opcion4_Click(object sender = null, RoutedEventArgs e = null)
        {
            _RespuestaElegida = 4;
            BotonOpcion4.Background = System.Windows.Media.Brushes.Gray;

            BotonOpcion1.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion2.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion3.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion5.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion6.Background = System.Windows.Media.Brushes.Black;
        }
        private void Opcion5_Click(object sender = null, RoutedEventArgs e = null)
        {
            _RespuestaElegida = 5;
            BotonOpcion5.Background = System.Windows.Media.Brushes.Gray;

            BotonOpcion1.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion2.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion3.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion4.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion6.Background = System.Windows.Media.Brushes.Black;
        }
        private void Opcion6_Click(object sender = null, RoutedEventArgs e = null)
        {
            _RespuestaElegida = 6;
            BotonOpcion6.Background = System.Windows.Media.Brushes.Gray;

            BotonOpcion1.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion2.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion3.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion4.Background = System.Windows.Media.Brushes.Black;
            BotonOpcion5.Background = System.Windows.Media.Brushes.Black;
        }

        #endregion
    }
}

