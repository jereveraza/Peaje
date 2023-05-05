using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using System.Configuration;
using System.Timers;
using ModuloPantallaTeclado.Clases;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Sub_Ventanas;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using Entidades.Comunicacion;
using Entidades.Logica;
using Entidades;
using System.Reflection;
using System.Diagnostics;
using Entidades.ComunicacionBaseDatos;
using Utiles;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Utiles.Utiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.Linq;
using Entidades.ComunicacionEventos;
using System.Collections.Specialized;

namespace ModuloPantallaTeclado.Pantallas
{
    public class ItemMsgSupervision
    {
        public string Mensaje { get; set; }
        public Brush Imagen { get; set; }
        public bool FlashMessage { get; set; }
    }

    /// <summary>
    /// Lógica de interacción para PantallaManual.xaml
    /// </summary>
    public partial class PantallaManual : Window, IPantalla
    {
        [DllImport( "user32.dll" )]
        static extern short VkKeyScan( char ch );

        #region Variables y propiedades de la clase
        private ISubVentana _subVentana = null;
        private object oLock = new object();
        private System.Timers.Timer _timerActualizaReloj = new System.Timers.Timer();
        private VentanaPrincipal _principal;
        private bool _teclado = true, _alarma = false;
        public Vehiculo VehiculoRecibido { set; get; }
        public Parte ParteRecibido { set; get; }
        public Turno TurnoRecibido { set; get; }
        public InformacionVia InformacionViaRecibida { set; get; }
        public Mimicos MimicosRecibidos { set; get; }
        public MensajesPantalla Mensajes = new MensajesPantalla();
        public ConfigVia ConfigViaRecibida { set; get; }
        private bool _bEstoyEsperandoRespuesta = false;
        public string ParametroAuxiliar { set; get; }
        private static System.Timers.Timer timeOutTimer;
        private const int comTimeOut = 1000;
        private IDictionary<enmTipoImagen, ImageBrush> _imagenesPantalla = new Dictionary<enmTipoImagen, ImageBrush>();
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        private bool _permiteCancelarConfirmacion = true, _estoyEsperandoConfirmacion = false;
        private Causa _causa = null;
        private ComandoLogica _comando = null;
        private string _auxString = null;

        private TextEffect tfe = new TextEffect();

        private string _caracterIncioCB;
        private eFinLecturaCB _eFinLectura;
        private object _finLectura;
        private bool _onLecturaCB = false;
        private System.Timers.Timer _timerLecturaCB = null;
        private string _codigoDeBarras = "";
        public event Action<TextCompositionEventArgs> LectorCodigoBarrasEvent;

        private System.Timers.Timer _timerBorraMsjSup = null;
        private List<Mensajes> listaMensajes = new List<Mensajes>();

        private VentanaFilaVehiculos _ventanaFilaVehiculos = null;
        private Point _posicionSubV;
        
        #endregion

        #region Constructor de la clase y metodo de carga de colores
        public PantallaManual(VentanaPrincipal principal)
        {
            InitializeComponent();
            _principal = principal;

            //Pongo el estilo cerrado si el turno esta abierto lo actualizo
            Estilo.UpdateResource(
                        ResourceList.BackgroundColor,
                        Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaCerradaBackgroundColor));
            Estilo.UpdateResource(
                ResourceList.BlockColor,
                Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaCerradaBlockColor));

            //Inicializo timer de actualizacion de hora
            _timerActualizaReloj = new System.Timers.Timer(500);
            _timerActualizaReloj.Elapsed += OnTick;
            _timerActualizaReloj.Enabled = true;
            _timerActualizaReloj.AutoReset = true;


            // Cada 50 minutos verifica si hay que borrar un msg de supervisión
            _timerBorraMsjSup = new System.Timers.Timer( 50 * 1000 * 60 );
            _timerBorraMsjSup.Elapsed += OnBorraMsgSup;
            _timerBorraMsjSup.Enabled = true;
            _timerBorraMsjSup.AutoReset = true;

            // Create and configure a simple color animation sequence.
            ColorAnimation blackToWhite = new ColorAnimation( Colors.Black, Colors.White, new Duration( new TimeSpan( 5000000 ) ) );
            blackToWhite.AutoReverse = true;
            blackToWhite.RepeatBehavior = RepeatBehavior.Forever;

            // Repite el comportamiento 10 segundos
            //blackToWhite.RepeatBehavior = new RepeatBehavior( new TimeSpan( 0, 0, 10 ) );

            // Create a new brush and apply the color animation.
            SolidColorBrush scb = new SolidColorBrush( Colors.White );
            scb.BeginAnimation( SolidColorBrush.ColorProperty, blackToWhite );

            // Set foreground brush to the previously created brush.
            tfe.Foreground = scb;
            
            // Range of text to apply effect to (all).
            tfe.PositionStart = 0;
            tfe.PositionCount = int.MaxValue;

            // The TextEffects property is null (no collection) by default.  Create a new one.
            txtMensajesVia1.TextEffects = new TextEffectCollection();
            txtMensajesVia2.TextEffects = new TextEffectCollection();
            txtMensajesVia3.TextEffects = new TextEffectCollection();

            //Cargo los eventos que llegan desde la pantalla principal
            _principal.KeyDown -= OnTecla;
            _principal.KeyDown += OnTecla;
            _principal.KeyUp -= OnTeclaUp;
            _principal.KeyUp += OnTeclaUp;

            //Evento de recepcion de codigo de barras
            LectorCodigoBarrasEvent -= OnLectorCodigoBarras;
            LectorCodigoBarrasEvent += OnLectorCodigoBarras;

            Clases.Utiles.TraducirControles<TextBlock>(gridPrincipal);

            //Me suscribo al evento de recepcion de datos y al de cambio de estado de conexion
            AsynchronousSocketListener.ProcesarDatosRecibidos += RecibirDatosLogica;
            AsynchronousSocketListener.ProcesarCambioEstadoConexion += CambioEstadoConexion;
            _principal.IniciarSocketServidor();

            CargarSubVentana(enmSubVentana.Principal);
            _subVentana = null;
            _bEstoyEsperandoRespuesta = false;
            ParametroAuxiliar = string.Empty;
            CargarColoresVentana();
            CargarConfigCB();
            CargarSubVentana(enmSubVentana.Categorias);
        }

        public void Dispose()
        {
            _timerBorraMsjSup?.Stop();
            _timerBorraMsjSup?.Dispose();

            _timerActualizaReloj?.Stop();
            _timerActualizaReloj?.Dispose();

            _principal.KeyDown -= OnTecla;
            _principal.KeyUp -= OnTeclaUp;
            
            LectorCodigoBarrasEvent -= OnLectorCodigoBarras;
            _logger.Debug("Dispose -> Dispose Timers");
        }

        private void CargarConfigCB()
        {
            _caracterIncioCB = ConfigurationManager.AppSettings["CAR_INICIO_CB"];
            string metodoFinLecturaCB = ConfigurationManager.AppSettings["FIN_LECTURA_CB"];

            _eFinLectura = (eFinLecturaCB) int.Parse( metodoFinLecturaCB );

            if(_eFinLectura == eFinLecturaCB.CaracterDeFin)
            {
                _finLectura = ConfigurationManager.AppSettings["CAR_FIN_CB"];
            }
            else if( _eFinLectura == eFinLecturaCB.Tiempo )
            {
#if !DEBUG
                _finLectura = Double.Parse( ConfigurationManager.AppSettings["TIEMPO_FIN_CB"] );
#else
                _finLectura = Double.Parse( ConfigurationManager.AppSettings["TIEMPO_FIN_CB"] ) * 10;
#endif

                _timerLecturaCB = new System.Timers.Timer((double)_finLectura);
                _timerLecturaCB.Elapsed += OnFinLecturaCB;
                _timerLecturaCB.Enabled = false;
                _timerLecturaCB.AutoReset = false;
            }
        }

        private void CargarColoresVentana()
        {
            //Cargo todas las imagenes desde la carpeta de recursos
            var carpeta = Convert.ToString(ConfigurationManager.AppSettings["CarpetaImagenes"]);
            var path = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + carpeta;
            _imagenesPantalla = CargarImagenesRecursos(path);

            //Muestro los logos en los borders correspondientes
            LogoTelectronica.Background = _imagenesPantalla[enmTipoImagen.LogoTelectronica];
            borderLogoCliente.Background = _imagenesPantalla[enmTipoImagen.LogoCliente];
        }
        #endregion

        #region Metodo para obtener el control (elemento del Framework)
        /// <summary>
        /// Obtiene el control que se quiere agregar a la ventana principal
        /// <returns>FrameworkElement</returns>
        /// </summary>
        public FrameworkElement ObtenerControlPrincipal()
        {
            FrameworkElement control = (FrameworkElement)borderManual.Child;
            borderManual.Child = null;
            Close();
            return control;
        }
        #endregion

        #region Mensaje de confirmacion
        const string msgConfirmar = "Confirme con {0}, {1} para volver.";
        const string msgSoloConfirmar = "Confirme con {0}.";
        private void MensajeConfirmacionALogica(string jsonOperacion)
        {
            _causa = ClassUtiles.ExtraerObjetoJson<Causa>(jsonOperacion);
            _auxString = string.Empty;

            if(_causa.Codigo == eCausas.CambiarFormaPago)
                _comando = ClassUtiles.ExtraerObjetoJson<ComandoLogica>(jsonOperacion);

            if ( _causa.Codigo == eCausas.AperturaTurno || _causa.Codigo == eCausas.AperturaTurnoMantenimiento || _causa.Codigo == eCausas.PagoTarjetaChip || _causa.Codigo == eCausas.SimulacionPaso)
            {
                _auxString = jsonOperacion;
            }

            _estoyEsperandoConfirmacion = true;

            if (_causa.Codigo == eCausas.ReincioViaFaltaLlave)
            {
                MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgSoloConfirmar),
                                Teclado.GetEtiquetaTecla("Enter")),
                                false
                                );
                _permiteCancelarConfirmacion = false;
                Application.Current.Dispatcher.Invoke((Action)(() =>
                { 
                    txtMensajesVia3.Text = _causa.Descripcion;
                }));
            }
            else
            {
                _permiteCancelarConfirmacion = true;

                //Borro mensajes previos de las lineas y escribo la causa
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    txtMensajesVia1.Text = _causa.Descripcion;
                    txtMensajesVia2.Text = string.Format(Traduccion.Traducir(msgConfirmar),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape"));
                    txtMensajesVia3.Text = string.Empty;
                }));
            }

            if (_causa.Codigo == eCausas.SimulacionPaso)
                _bEstoyEsperandoRespuesta = false;
        }
        #endregion

        #region Metodo de analisis de comandos recibidos desde logica
        private void AnalizarComandoMenu(ComandoLogica com)
        {
            if (com.Accion == enmAccion.MUESTRA_MENU && com.Operacion != string.Empty)
            {
                _bEstoyEsperandoRespuesta = false;
                ParametroAuxiliar = com.Operacion;
                CargarSubVentana(enmSubVentana.MenuPrincipal);
            }
            else if (com.Accion == enmAccion.MIMICOS && com.Operacion != string.Empty)
            {
                Application.Current.Dispatcher.Invoke((Action)(() => OnRecibeMimicosDispositivos(com.Operacion)));
            }
            else if(com.Accion == enmAccion.FILAVEHICULOS && com.Operacion != string.Empty)
            {
                if (_ventanaFilaVehiculos != null && _ventanaFilaVehiculos.TieneFoco)
                {
                    _ventanaFilaVehiculos.RecibirDatosLogica(com);
                }
            }
        }
        
        /// <summary>
        /// Analiza y procesa cada comando recibido
        /// </summary>
        /// <param name="com"></param>
        private void AnalizoDatosRecibidos( ComandoLogica com )
        {
            //_logger.Trace( "AnalizoDatosRecibidos -> _bEstoyEsperandoRespuesta[{0}]", _bEstoyEsperandoRespuesta );

            if( com.CodigoStatus == enmStatus.Abortada )
            {
                _bEstoyEsperandoRespuesta = false;
                return;
            }

            // Si se pulso tecla, se utiliza este flag para timeout
            if( _bEstoyEsperandoRespuesta )
            {
                if( com.Accion == enmAccion.T_MENU && com.Operacion != string.Empty )
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana( enmSubVentana.MenuPrincipal );
                    _bEstoyEsperandoRespuesta = false;
                }
                else if( com.Accion == enmAccion.T_PATENTE )
                {
                    
                    ParametroAuxiliar = com.Operacion;
                    _bEstoyEsperandoRespuesta = false;
                    CargarSubVentana( enmSubVentana.Patente );
                }
                else if (com.Accion == enmAccion.T_CATEGORIAESPECIAL)
                {

                    ParametroAuxiliar = com.Operacion;
                    _bEstoyEsperandoRespuesta = false;
                    CargarSubVentana(enmSubVentana.CantEjes);
                }                
                else if( com.Accion == enmAccion.T_OBSERVACIONES && com.Operacion != string.Empty )
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana( enmSubVentana.MenuPrincipal );
                    _bEstoyEsperandoRespuesta = false;
                }

                else if( com.Accion == enmAccion.T_MONEDAEXTRA && com.Operacion != string.Empty )
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana( enmSubVentana.MonedaExtranjera );
                    _bEstoyEsperandoRespuesta = false;
                }
                else if( com.Accion == enmAccion.T_RETIRO && com.Operacion != string.Empty )
                {
                    ParametroAuxiliar = com.Operacion;
                    if( ParametroAuxiliar == enmAccion.LOGIN.ToString() )
                        CargarSubVentana( enmSubVentana.IngresoSistema );
                    else
                        CargarSubVentana( enmSubVentana.MenuPrincipal );
                    _bEstoyEsperandoRespuesta = false;
                }
                else if( com.Accion == enmAccion.T_PAGODIFERIDO )
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana( enmSubVentana.CrearPagoDiferido );
                    _bEstoyEsperandoRespuesta = false;
                }
                /*else if( com.Accion == enmAccion.T_TICKETMANUAL && com.Operacion == string.Empty )
                {
                    CargarSubVentana( enmSubVentana.TicketManual );
                    _bEstoyEsperandoRespuesta = false;
                }*/
                else if( (com.Accion == enmAccion.T_VALEPREPAGO || com.Accion == enmAccion.T_AUTORIZACIONPASO) && com.Operacion != string.Empty )
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana( enmSubVentana.AutorizacionPasoVale);
                    _bEstoyEsperandoRespuesta = false;
                }
                else if( com.Accion == enmAccion.FACTURA && com.CodigoStatus == enmStatus.Ok )
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana( enmSubVentana.Factura );
                    _bEstoyEsperandoRespuesta = false;
                }
                else if (com.Accion == enmAccion.LIST_EXENTO)
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana(enmSubVentana.Exento);
                    _bEstoyEsperandoRespuesta = false;
                }
                else if (com.Accion == enmAccion.PAGO_DIF)
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana(enmSubVentana.CrearPagoDiferido);
                    _bEstoyEsperandoRespuesta = false;
                }
            }
            else
            {
                if( com.Accion == enmAccion.FONDO_CAMBIO && com.Operacion != string.Empty )
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana( enmSubVentana.FondoCambio );
                }
                else if( com.Accion == enmAccion.RETIRO_ANT && com.Operacion != string.Empty )
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana( enmSubVentana.RetiroAnticipado );
                }
                else if (com.Accion == enmAccion.T_VUELTO && com.Operacion == "[]")
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana(enmSubVentana.Vuelto);
                    _bEstoyEsperandoRespuesta = false;
                }
                else if (com.Accion == enmAccion.T_VUELTO && com.Operacion.Contains("Decimal"))
                {
                    decimal vuelto = ClassUtiles.ExtraerObjetoJson<decimal>(com.Operacion);
                    int monto = ClassUtiles.ExtraerObjetoJson<int>(com.Operacion);
                    if(monto>=0)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            txtMonto.Text = "R:S/" + monto.ToString("0.00");
                        });
                    }
                    else
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            txtMonto.Text = "R:S/0.00";
                        });
                    }

                    if (vuelto >= 0)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            txtVuelto.Text = "V:S/" + vuelto.ToString("0.00");
                        });
                    }
                    else
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            txtVuelto.Text = "V:S/0.00";
                        });
                    }
                    CargarSubVentana(enmSubVentana.Principal);
                   
                }
                else if (com.Accion == enmAccion.FACTURA && com.CodigoStatus == enmStatus.Ok)
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana(enmSubVentana.Factura);
                    _bEstoyEsperandoRespuesta = false;
                }
                else if( com.Accion == enmAccion.LIQUIDACION && com.Operacion != string.Empty )
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana( enmSubVentana.Liquidacion );
                }
                else if (com.Accion == enmAccion.COBRODEUDAS)
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana(enmSubVentana.CobroDeudas);
                }
                else if (com.Accion == enmAccion.PAGO_DIF)
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana(enmSubVentana.CrearPagoDiferido);
                }
                else if( com.Accion == enmAccion.RECORRIDO && com.Operacion != string.Empty )
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana( enmSubVentana.Recorridos );
                }
                else if( com.Accion == enmAccion.MSG_SUPER && com.Operacion != string.Empty )
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana( enmSubVentana.MenuPrincipal );
                }
                else if( com.Accion == enmAccion.VIAESTACION )
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana( enmSubVentana.ViaEstacion );
                }
                else if( com.Accion == enmAccion.LIST_EXENTO )
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana( enmSubVentana.Exento );
                }
                else if ( com.Accion == enmAccion.CORRER_PROCESO )
                {
                    Proceso proceso = JsonConvert.DeserializeObject<Proceso>( com.Operacion );

                    CorrerProceso( proceso.Opcion );
                }
                else if ((com.Accion == enmAccion.T_VALEPREPAGO || com.Accion == enmAccion.T_AUTORIZACIONPASO) && com.Operacion != string.Empty)
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana(enmSubVentana.AutorizacionPasoVale);
                }
                else if (com.Accion == enmAccion.VERSIONES)
                {
                    ParametroAuxiliar = com.Operacion;
                    CargarSubVentana(enmSubVentana.Versiones);
                }
            }

            if (com.Accion == enmAccion.LOGIN && com.Operacion != string.Empty)
            {
                ParametroAuxiliar = com.Operacion;
                CargarSubVentana(enmSubVentana.IngresoSistema);
            }
            else if (com.Accion == enmAccion.MENSAJES && com.Operacion != string.Empty)
            {
                OnMensaje(com.Operacion);
            }
            else if (com.Accion == enmAccion.DATOS_VIA ||
                    com.Accion == enmAccion.DATOS_TURNO ||
                    com.Accion == enmAccion.DATOS_PARTE ||
                    com.Accion == enmAccion.DATOS_VEHICULO
                    && com.Operacion != string.Empty)
            {
                OnDatosVia(com.Accion, com.Operacion);
            }
            else if (com.Accion == enmAccion.ACTUALIZA_ULTVEH && com.CodigoStatus == enmStatus.Ok)
            {
                Application.Current.Dispatcher.Invoke((Action)(() => OnActualizaUltimoVeh(com.Operacion)));
            }
            
            else if (com.Accion == enmAccion.CONFIRMAR && com.CodigoStatus == enmStatus.Ok)
            {
                //ParametroAuxiliar = com.Operacion;
                //CargarSubVentana( enmSubVentana.VentanaConfirmacion );

                MensajeConfirmacionALogica(com.Operacion);
            }
            else if (com.Accion == enmAccion.NUMERACION && com.CodigoStatus == enmStatus.Ok)
            {
                ParametroAuxiliar = com.Operacion;
                CargarSubVentana(enmSubVentana.AutorizacionNumeracion);
            }
            else if (com.Accion == enmAccion.TAGMANUAL)
            {
                ParametroAuxiliar = com.Operacion;
                CargarSubVentana(enmSubVentana.TagOcrManual);
                _bEstoyEsperandoRespuesta = false;
            }
            else if (com.Accion == enmAccion.RECARGA && com.Operacion != string.Empty)
            {
                ParametroAuxiliar = com.Operacion;
                CargarSubVentana(enmSubVentana.Venta);
                _bEstoyEsperandoRespuesta = false;
            }
            else if (com.Accion == enmAccion.CAMBIO_PASS && com.Operacion != string.Empty)
            {
                ParametroAuxiliar = com.Operacion;
                CargarSubVentana(enmSubVentana.CambioPassword);
            }
            else if (com.Accion == enmAccion.FOTO && com.Operacion != string.Empty)
            {
                ParametroAuxiliar = com.Operacion;
                CargarSubVentana(enmSubVentana.Foto);
                _bEstoyEsperandoRespuesta = false;
            }
            else if (com.Accion == enmAccion.EXPRESIONES && com.Operacion != string.Empty)
            {
                Clases.Utiles.CargarRegex(com.Operacion);
                _logger.Info("Se obtiene lista de Expresiones regulares");
            }
            else if (com.Accion == enmAccion.SIMBOLOS && com.Operacion != string.Empty)
            {
                Clases.Utiles.CargarSimbolos(com.Operacion);
                _logger.Info("Se obtiene lista de Simbolos");
            }
            else if (com.Accion == enmAccion.CONFIGTECLAS && com.Operacion != string.Empty)
            {
                Dictionary<string, string> sectorTeclas = Utiles.ClassUtiles.ExtraerObjetoJson<Dictionary<string, string>>(com.Operacion);
                Teclado.CargarTeclasFuncion(ref sectorTeclas);
                ActualizarVersiones();
            }
            else if (com.Accion == enmAccion.T_PATENTE)
            {
                ParametroAuxiliar = com.Operacion;
                CargarSubVentana(enmSubVentana.Patente);
                _bEstoyEsperandoRespuesta = false;
            }
            else if (com.Accion == enmAccion.T_CATEGORIAESPECIAL)
            {
                ParametroAuxiliar = com.Operacion;
                CargarSubVentana(enmSubVentana.CantEjes);
                _bEstoyEsperandoRespuesta = false;
            }
            else if (com.Accion == enmAccion.T_CATEGORIAS)
            {
                ParametroAuxiliar = com.Operacion;
                CargarSubVentana(enmSubVentana.Categorias);
                _bEstoyEsperandoRespuesta = false;
                TecladoOculto();
            }
            else if (com.Accion == enmAccion.T_ENCUESTA)
            {
                ParametroAuxiliar = com.Operacion;
                CargarSubVentana(enmSubVentana.EncuestaUsuarios);
                _bEstoyEsperandoRespuesta = false;
                TecladoOculto();
            }
            else if (com.Accion == enmAccion.T_FORMAPAGO)
            {
                ParametroAuxiliar = com.Operacion;
                CargarSubVentana(enmSubVentana.FormaPago);
                _bEstoyEsperandoRespuesta = false;
                TecladoOculto();
            }
            else if ((com.Accion == enmAccion.T_TICKETMANUAL || com.Accion == enmAccion.T_COMITIVA)
                    && com.Operacion != string.Empty)
            {
                ParametroAuxiliar = com.Operacion;
                CargarSubVentana(enmSubVentana.TicketManualComitiva);
                _bEstoyEsperandoRespuesta = false;
            }
            else if (com.Accion == enmAccion.PATENTE_OCR && com.Operacion != string.Empty)
            {
                Vehiculo vehiculo = ClassUtiles.ExtraerObjetoJson<Vehiculo>(com.Operacion);
                if (!string.IsNullOrEmpty(vehiculo.InfoOCRDelantero.Patente))
                {
                    txtPatente.Dispatcher.Invoke((Action)(() =>
                    {
                        txtPatenteOCR.Text = vehiculo.InfoOCRDelantero.Patente;
                    }));
                }
            }
            else if (com.Accion == enmAccion.SET_FOCUS)
            {
                _principal.SetWindowFocus();
            }
        }

        private void borderLogoTele_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 3)
            {
                _principal.SetVentanaMaximizada();
            }
        }

        private void OnActualizaUltimoVeh( string objDatos )
        {
            txtUltimoTransito.Text = string.Empty;

            Vehiculo vehiculo = ClassUtiles.ExtraerObjetoJson<Vehiculo>( objDatos );

            string strAux = string.Empty;

            if( vehiculo.TipoViolacion == ' ' || vehiculo.CodigoSimulacion != 0 )
            {
                strAux = Traduccion.Traducir( ClassUtiles.GetDescription( vehiculo.FormaPago ) )
                + " - " + vehiculo.CategoDescripcionLarga;
            }
            else
            {
                string cat = string.Empty;

                if( string.IsNullOrEmpty( vehiculo.CategoDescripcionLarga ) )
                    cat = vehiculo.InfoDac.Categoria > 0 ? "CAT " + vehiculo.InfoDac.Categoria.ToString() : Traduccion.Traducir("SIN PEANAS");
                else
                    cat = vehiculo.CategoDescripcionLarga;

                strAux = Traduccion.Traducir( "Violación" ) + " - " + cat;
            }

            if( !string.IsNullOrEmpty( vehiculo.Patente ) )
                strAux += " - " + vehiculo.Patente;

            strAux += " - " + /*vehiculo.Get_Fecha() + " " +*/ vehiculo.Get_Hora();

            if( !string.IsNullOrEmpty( vehiculo.InfoTag.NumeroTag ) )
                strAux += " - " + vehiculo.InfoTag.NumeroTag;

            if( vehiculo.CodigoSimulacion != 0 )
                strAux += " (SIP)";
            else if( vehiculo.Abortada )
                strAux += " (ABORTADO)";

            txtUltimoTransito.Text = strAux;
        }

        private void OnDatosVia(enmAccion accion,string objDatos)
        {
            InformacionVia informacionViaRecibida = new InformacionVia();
            InformacionViaRecibida = informacionViaRecibida;

            ClienteDB clienteVehiculo = null;

            //Extraigo los datos en sus respectivas propiedades
            switch (accion)
            {                
                case enmAccion.DATOS_VIA:
                    ConfigViaRecibida = ClassUtiles.ExtraerObjetoJson<ConfigVia>(objDatos);

                    InformacionViaRecibida.ConfigVia = ConfigViaRecibida;

                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        txtEstacion.Text = string.Format("{0} - {1}", ConfigViaRecibida.CodigoEstacion, ConfigViaRecibida.NombreEstacion);
                        txtVia.Text = ConfigViaRecibida.NombreVia;

                        if (ConfigViaRecibida.TieneOCR == 'S')
                            stackPatenteOCR.Visibility = Visibility.Visible;

                        if(ConfigViaRecibida.TChip != "S" && imgTarjChipBorder.IsVisible)
                        {
                            imgTarjChipBorder.Visibility = Visibility.Hidden;
                            txtTarjChip.Visibility = Visibility.Hidden;
                        }
                        else if(ConfigViaRecibida.TChip == "S" && !imgTarjChipBorder.IsVisible)
                        {
                            imgTarjChipBorder.Visibility = Visibility.Visible;
                            txtTarjChip.Visibility = Visibility.Visible;
                        }
                    }));
                    
                    break;
                case enmAccion.DATOS_VEHICULO:
                    VehiculoRecibido = ClassUtiles.ExtraerObjetoJson<Vehiculo>(objDatos);
                    clienteVehiculo = ClassUtiles.ExtraerObjetoJson<ClienteDB>(objDatos);

                    if(VehiculoRecibido.Categoria > 0)
                        _logger.Info("Datos Vehiculo CAT[{0}], TARIFA [{1}], NROVEH[{2}]", VehiculoRecibido.Categoria, VehiculoRecibido.Tarifa, VehiculoRecibido.NumeroVehiculo);

                    if ( !string.IsNullOrEmpty( VehiculoRecibido?.InfoTag?.NumeroTag ) )
                        _logger.Info( "Muestro vehiculo con TAG [{0}], PAT [{1}], NROVEH[{2}]", VehiculoRecibido.InfoTag.NumeroTag, VehiculoRecibido.InfoTag.Patente, VehiculoRecibido.NumeroVehiculo );

                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        if (VehiculoRecibido != null)
                        {
                            
                            if (VehiculoRecibido.BorrarNumTicket)
                            {
                                txtNumeroFactura.Text = "";
                                txtNumeroBoleta.Text = "";
                            }
                                

                            //si es mantenimiento muestro el NF
                            if (TurnoRecibido?.Mantenimiento == 'S')
                            {
                                if (VehiculoRecibido.NumeroTicketNF != 0)
                                    txtNumeroBoleta.Text = VehiculoRecibido.NumeroTicketNF.ToString();
                            }
                            else
                            {
                                if (VehiculoRecibido.NumeroTicketF != 0 )
                                    txtNumeroBoleta.Text = VehiculoRecibido.NumeroTicketF.ToString();
                                else if (VehiculoRecibido.NumeroFactura != 0)
                                    txtNumeroFactura.Text = VehiculoRecibido.NumeroFactura.ToString();
                            }

                            if (VehiculoRecibido.NumeroTransito != 0)
                                txtNumeroTransito.Text = VehiculoRecibido.NumeroTransito.ToString();

                            if (!string.IsNullOrEmpty(VehiculoRecibido.Patente))
                            {
                                txtPatente.Text = VehiculoRecibido.Patente;
                            }
                            else
                            {
                                txtPatente.Text = string.Empty;
                            }

                            if (!string.IsNullOrEmpty(VehiculoRecibido.InfoOCRDelantero.Patente))
                            {
                                txtPatenteOCR.Text = VehiculoRecibido.InfoOCRDelantero.Patente;
                            }
                            else
                            {
                                txtPatenteOCR.Text = string.Empty;
                            }

                            if (VehiculoRecibido.InfoTag != null)
                            {
                                //txtNumeroRuc.Text = VehiculoRecibido.InfoTag.Ruc;
                                txtNombreRazonSocial.Text = VehiculoRecibido.InfoTag.NombreCuenta;
                            }
                            else
                            {
                                //txtNumeroRuc.Text = string.Empty;
                                txtNombreRazonSocial.Text = string.Empty;
                            }

                            if (VehiculoRecibido.FormaPago != eFormaPago.Nada || VehiculoRecibido.TipoViolacion != ' ')
                            {
                                string strAux = string.Empty;

                                if( VehiculoRecibido.TipoViolacion == ' ' )
                                {
                                    //txtFormaPago.Text = Traduccion.Traducir( VehiculoRecibido.FormaPago.GetDescription() );

                                    switch (VehiculoRecibido.TipOp)
                                    {
                                        case 'E':
                                            txtFormaPagoVisible.Text = Traduccion.Traducir("Efectivo");
                                            break;
                                        case 'X':
                                            txtFormaPagoVisible.Text = Traduccion.Traducir("Exento");
                                            break;
                                        case 'T':
                                        case 'O':
                                            if (VehiculoRecibido.TipBo == 'C')
                                                txtFormaPagoVisible.Text = Traduccion.Traducir("TSC");
                                            else
                                                txtFormaPagoVisible.Text = Traduccion.Traducir("TAG");
                                            break;
                                        default:
                                            txtFormaPagoVisible.Text = string.Empty;
                                            break;
                                    }

                                    if ( VehiculoRecibido.EstaPagado )
                                    {
                                        if( VehiculoRecibido.NumeroTicketF != 0)
                                            txtNumeroBoleta.Text = VehiculoRecibido.NumeroTicketF.ToString();
                                        else if (VehiculoRecibido.NumeroFactura != 0)
                                            txtNumeroFactura.Text = VehiculoRecibido.NumeroFactura.ToString();
                                        if ( VehiculoRecibido.NumeroTransito != 0 )
                                            txtNumeroTransito.Text = VehiculoRecibido.NumeroTransito.ToString();
                                    }
                                }
                            }
                            else
                            {
                                //txtFormaPago.Text = string.Empty;
                                txtFormaPagoVisible.Text = string.Empty;
                            }

                            if (VehiculoRecibido.Tarifa != 0 && VehiculoRecibido.TipoViolacion == ' ')
                            {
                                txtTarifa.Text = Datos.FormatearMonedaAString(VehiculoRecibido.Tarifa);
                                txtNumeroCategoria.Text = VehiculoRecibido.CategoDescripcionLarga;
                                imgCategoria.Background = GetImagenCategoria(VehiculoRecibido);
                                if(VehiculoRecibido.FormaPago == eFormaPago.Nada && VehiculoRecibido.InfoTag.Patente == string.Empty)
                                    CargarSubVentana(enmSubVentana.FormaPago);
                                if (VehiculoRecibido.AfectaDetraccion == 'S' && VehiculoRecibido.EstadoDetraccion == 3)
                                {
                                    txtDetraccion.Visibility = Visibility.Visible;
                                    txtValorDetraccion.Text = Datos.FormatearMonedaAString(VehiculoRecibido.ValorDetraccion);
                                }
                                else
                                    txtDetraccion.Visibility = Visibility.Hidden;
                            }
                            else
                            {
                                imgCategoria.Background = null;
                                txtTarifa.Text = string.Empty;
                                txtNumeroCategoria.Text = string.Empty;
                                txtValorDetraccion.Text = string.Empty;
                                txtDetraccion.Visibility = Visibility.Hidden;
                            }
                        }

                        if (clienteVehiculo != null)
                        {
                            //txtNumeroRuc.Text = clienteVehiculo.Get_Ruc();
                            txtNombreRazonSocial.Text = clienteVehiculo.Get_RazonSocial();
                        }
                    }));

                    break;
                case enmAccion.DATOS_PARTE:
                    ParteRecibido = ClassUtiles.ExtraerObjetoJson<Parte>(objDatos);
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        if (TurnoRecibido?.EstadoTurno == enmEstadoTurno.Cerrada)
                            txtParte.Text = string.Empty;
                        else
                        {
                            if (TurnoRecibido?.Parte != null)
                            {
                                if (TurnoRecibido.Parte.NumeroParte == 0)
                                    txtParte.Text = Traduccion.Traducir("Sin Parte");
                                else
                                    txtParte.Text = TurnoRecibido.Parte.NumeroParte.ToString();
                                CargarSubVentana(enmSubVentana.Categorias);
                            }
                        }
                    }));
                    break;
                case enmAccion.DATOS_TURNO:
                    TurnoRecibido = ClassUtiles.ExtraerObjetoJson<Turno>(objDatos);           

                    if (TurnoRecibido.Mantenimiento == 'S' && TurnoRecibido.EstadoTurno == enmEstadoTurno.Abierta)
                    {
                        Estilo.UpdateResource(ResourceList.BackgroundColor,Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaMantenimientoBackgroundColor));
                        Estilo.UpdateResource(ResourceList.BlockColor,Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaMantenimientoBlockColor));
                        Estilo.UpdateResource(ResourceList.ValueFontColor, Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaMantenimientoValueFontColor));
                        Estilo.UpdateResource(ResourceList.LabelFontColor, Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaMantenimientoLabelFontColor));
                        Estilo.UpdateResource(ResourceList.LabelFontColor2, Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaMantenimientoLabelFontColor2));
                    }
                    else if( TurnoRecibido.EstadoTurno == enmEstadoTurno.Abierta)
                    {
                        Estilo.UpdateResource( ResourceList.BackgroundColor, Estilo.FindResource<SolidColorBrush>( ResourceList.EstadoViaAbiertaBackgroundColor ) );
                        Estilo.UpdateResource(ResourceList.BlockColor, Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaAbiertaBlockColor));
                        
                        var converter = new BrushConverter();
                        try
                        {
                            NameValueCollection color = (NameValueCollection)ConfigurationManager.GetSection("color");
                            if (!string.IsNullOrEmpty(color["fondo"]) && color["fondo"].Contains('#'))
                            {
                                var brush = (Brush)converter.ConvertFromString(color["fondo"]);
                                Estilo.UpdateResource(ResourceList.BackgroundColor, brush);
                                Estilo.UpdateResource(ResourceList.BlockColor, brush);
                            }
                            if (!string.IsNullOrEmpty(color["letra"]) && color["letra"].Contains('#'))
                            {
                                var brush = (Brush)converter.ConvertFromString(color["letra"]);
                                Estilo.UpdateResource(ResourceList.ValueFontColor, brush);
                                Estilo.UpdateResource(ResourceList.LabelFontColor, brush);
                                Estilo.UpdateResource(ResourceList.LabelFontColor2, brush);
                            }
                            CargarSubVentana(enmSubVentana.Categorias);

                        }
                        catch
                        {

                        }
                        //Estilo.UpdateResource( ResourceList.ValueFontColor, Estilo.FindResource<SolidColorBrush>( ResourceList.EstadoViaAbiertaValueFontColor ) );
                        //Estilo.UpdateResource( ResourceList.LabelFontColor, Estilo.FindResource<SolidColorBrush>( ResourceList.EstadoViaAbiertaLabelFontColor ) );
                        //Estilo.UpdateResource( ResourceList.LabelFontColor2, Estilo.FindResource<SolidColorBrush>( ResourceList.EstadoViaAbiertaLabelFontColor2 ) );
                    }
                    else if ( TurnoRecibido.EstadoTurno == enmEstadoTurno.Quiebre)
                    {
                        Estilo.UpdateResource(ResourceList.BackgroundColor,Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaQuiebreBackgroundColor ));
                        Estilo.UpdateResource(ResourceList.BlockColor,Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaQuiebreBlockColor ) );
                        Estilo.UpdateResource(ResourceList.ValueFontColor, Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaQuiebreValueFontColor ) );
                        Estilo.UpdateResource(ResourceList.LabelFontColor, Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaQuiebreLabelFontColor ) );
                        Estilo.UpdateResource(ResourceList.LabelFontColor2, Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaQuiebreLabelFontColor2 ) );
                    }
                    else
                    {
                        Estilo.UpdateResource(ResourceList.BackgroundColor,Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaCerradaBackgroundColor));
                        Estilo.UpdateResource(ResourceList.BlockColor,Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaCerradaBlockColor));
                        Estilo.UpdateResource(ResourceList.ValueFontColor, Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaCerradaValueFontColor));
                        Estilo.UpdateResource(ResourceList.LabelFontColor, Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaCerradaLabelFontColor));
                        Estilo.UpdateResource(ResourceList.LabelFontColor2, Estilo.FindResource<SolidColorBrush>(ResourceList.EstadoViaCerradaLabelFontColor2));
                    }
                    
                    InformacionViaRecibida.Turno = TurnoRecibido;
                    ConfigViaRecibida = ClassUtiles.ExtraerObjetoJson<ConfigVia>(objDatos);
                    InformacionViaRecibida.ConfigVia = ConfigViaRecibida;

                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        if (TurnoRecibido.Mantenimiento == 'S')
                            txtFactura.Text = Traduccion.Traducir("Ticket");
                        else if (txtFactura.Text != Traduccion.Traducir("Factura"))
                            txtFactura.Text = Traduccion.Traducir("Factura");

                        txtBoleta.Text = Traduccion.Traducir("Boleta");

                        txtModoVia.Text = Traduccion.Traducir(TurnoRecibido.ModoVia);
                        txtEstadoVia.Text = Traduccion.Traducir(TurnoRecibido.EstadoViaPantalla);

                        if (TurnoRecibido?.Parte?.NombreCajero != null)
                            txtCajero.Text = TurnoRecibido?.Parte.NombreCajero.ToUpper();
                        else
                            txtCajero.Text = string.Empty;

                        if (TurnoRecibido?.EstadoTurno == enmEstadoTurno.Cerrada)
                            txtParte.Text = string.Empty;
                        else
                        {
                            if (TurnoRecibido?.Parte != null)
                            {
                                if (TurnoRecibido.Parte.NumeroParte == 0)
                                    txtParte.Text = Traduccion.Traducir("Sin Parte");
                                else
                                    txtParte.Text = TurnoRecibido.Parte.NumeroParte.ToString();
                            }
                        }
                    }));
                    break;
            }
        }
        #endregion

        #region Metodo de recepcion de mensajes
        /// <summary>
        /// Metodo que actualiza los mensajes en pantalla
        /// (Mensajes de lineas y desde supervision)
        /// </summary>
        /// <param name="objMensaje"></param>
        private void OnMensaje(string objMensaje)
        {
            try
            {
                var MensajeRecibido = JsonConvert.DeserializeObject<Mensajes>(objMensaje);

                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    ItemMsgSupervision ItemMsgSupervision = null;
                    Mensajes.ActualizarMensaje(MensajeRecibido);
                    switch (MensajeRecibido.TipoMensaje)
                    {
                        case enmTipoMensaje.Linea1:
                            {
                                if( txtMensajesVia1.Text == MensajeRecibido.Mensaje && !string.IsNullOrEmpty( MensajeRecibido.Mensaje ) && MensajeRecibido.PuedeTitilar )
                                {
                                    txtMensajesVia1.TextEffects.Add( tfe );
                                }
                                else
                                {
                                    txtMensajesVia1.TextEffects.Clear();

                                    txtMensajesVia1.Text = MensajeRecibido.Mensaje;
                                }
                                break;
                            }
                        case enmTipoMensaje.Linea2:
                            {
                                if( txtMensajesVia2.Text == MensajeRecibido.Mensaje && !string.IsNullOrEmpty( MensajeRecibido.Mensaje ) && MensajeRecibido.PuedeTitilar )
                                {
                                    txtMensajesVia2.TextEffects.Add( tfe );
                                }
                                else
                                {
                                    txtMensajesVia2.TextEffects.Clear();

                                    txtMensajesVia2.Text = MensajeRecibido.Mensaje;
                                }
                                break;
                            }
                        case enmTipoMensaje.Linea3:
                            {
                                if( txtMensajesVia3.Text == MensajeRecibido.Mensaje && !string.IsNullOrEmpty( MensajeRecibido.Mensaje ) && MensajeRecibido.PuedeTitilar )
                                {
                                    txtMensajesVia3.TextEffects.Add( tfe );
                                }
                                else
                                {
                                    txtMensajesVia3.TextEffects.Clear();

                                    txtMensajesVia3.Text = MensajeRecibido.Mensaje;
                                }
                                break;
                            }
                        case enmTipoMensaje.LimpiaMsgSupervision:
                            {
                                ListBoxMsgSupervision.Items.Clear();
                                break;
                            }
                        case enmTipoMensaje.MsgSupRecibido:
                            {
                                ItemMsgSupervision = new ItemMsgSupervision();
                                ItemMsgSupervision.FlashMessage = true;
                                ItemMsgSupervision.Imagen = _imagenesPantalla[enmTipoImagen.MsgRecibido];
                                goto case enmTipoMensaje.CmdSupervision;
                            }
                        case enmTipoMensaje.MsgSupEnviado:
                            {
                                ItemMsgSupervision = new ItemMsgSupervision();
                                ItemMsgSupervision.FlashMessage = false;
                                ItemMsgSupervision.Imagen = _imagenesPantalla[enmTipoImagen.MsgEnviado];
                                goto case enmTipoMensaje.CmdSupervision;
                            }
                        case enmTipoMensaje.CmdSupervision:
                            {
                                if (!string.IsNullOrEmpty(MensajeRecibido.Mensaje))
                                {
                                    listaMensajes.Add( MensajeRecibido );
                                    if (ItemMsgSupervision == null)
                                    {
                                        ItemMsgSupervision = new ItemMsgSupervision();
                                        ItemMsgSupervision.FlashMessage = false;
                                        ItemMsgSupervision.Imagen = _imagenesPantalla[enmTipoImagen.MsgComando];
                                    }
                                    ItemMsgSupervision.Mensaje = MensajeRecibido.Mensaje;

                                    ListBoxMsgSupervision.Items.Insert( 0 , ItemMsgSupervision );
                                    if ( ListBoxMsgSupervision.Items.Count > 3 )
                                    {
                                        ListBoxMsgSupervision.Items.RemoveAt( 3 );
                                        listaMensajes.RemoveAt( 0 );
                                    }
                                    Scroll.ToBottom( ListBoxMsgSupervision );
                                }
                                break;
                            }
                    }
                }));
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("PantallaManual:OnMensaje() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("PantallaManual:OnMensaje() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
                _logger.Error(ex);
            }
        }
        #endregion

        #region Metodo de actualizacion de mimicos de dispositivos
        /// <summary>
        /// Carga las imagenes correspondientes al estado de cada dispositivo
        /// </summary>
        /// <param name="objMimicosJson"></param>
        private void OnRecibeMimicosDispositivos(string objMimicosJson)
        {
            try
            {
                MimicosRecibidos = ClassUtiles.ExtraerObjetoJson<Mimicos>(objMimicosJson);

                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (MimicosRecibidos.EstadoRedLocal != enmEstadoDispositivo.Nada)
                    {
                        if (MimicosRecibidos.EstadoRedLocal == enmEstadoDispositivo.OK)
                        {
                            imgEstadoRedLocalBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgEstadoRedLocal.Background = _imagenesPantalla[enmTipoImagen.RedOk];
                        }
                        else if (MimicosRecibidos.EstadoRedLocal == enmEstadoDispositivo.Error)
                        {
                            imgEstadoRedLocalBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleError);
                            imgEstadoRedLocal.Background = _imagenesPantalla[enmTipoImagen.RedError];
                        }
                        else if(MimicosRecibidos.EstadoRedLocal == enmEstadoDispositivo.Warning)
                        {
                            imgEstadoRedLocalBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleWarning);
                            imgEstadoRedLocal.Background = _imagenesPantalla[enmTipoImagen.RedWarning];
                        }
                        else if (MimicosRecibidos.EstadoRedLocal == enmEstadoDispositivo.No)
                        {
                            imgEstadoRedLocalBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgEstadoRedLocal.Background = _imagenesPantalla[enmTipoImagen.RedNo];
                        }
                    }

                    if (MimicosRecibidos.EstadoRedServidor != enmEstadoDispositivo.Nada)
                    {
                        if (MimicosRecibidos.EstadoRedServidor == enmEstadoDispositivo.OK)
                        {
                            imgEstadoRedEstBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgEstadoRedEst.Background = _imagenesPantalla[enmTipoImagen.RedOk];
                        }
                        else if (MimicosRecibidos.EstadoRedServidor == enmEstadoDispositivo.Error)
                        {
                            imgEstadoRedEstBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleError);
                            imgEstadoRedEst.Background = _imagenesPantalla[enmTipoImagen.RedError];
                        }
                        else if (MimicosRecibidos.EstadoRedServidor == enmEstadoDispositivo.Warning)
                        {
                            imgEstadoRedEstBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleWarning);
                            imgEstadoRedEst.Background = _imagenesPantalla[enmTipoImagen.RedWarning];
                        }
                        else if (MimicosRecibidos.EstadoRedServidor == enmEstadoDispositivo.No)
                        {
                            imgEstadoRedEstBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgEstadoRedEst.Background = _imagenesPantalla[enmTipoImagen.RedNo];
                        }
                    }

                    if (MimicosRecibidos.EstadoImpresora != enmEstadoImpresora.Nada)
                    {
                        if (MimicosRecibidos.EstadoImpresora == enmEstadoImpresora.OK)
                        {
                            imgPrinterBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgPrinter.Background = _imagenesPantalla[enmTipoImagen.ImpresoraOk];
                        }
                        else if (MimicosRecibidos.EstadoImpresora == enmEstadoImpresora.Error)
                        {
                            imgPrinterBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleError);
                            imgPrinter.Background = _imagenesPantalla[enmTipoImagen.ImpresoraError];
                        }
                        else if (MimicosRecibidos.EstadoImpresora == enmEstadoImpresora.No)
                        {
                            imgPrinterBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgPrinter.Background = _imagenesPantalla[enmTipoImagen.ImpresoraNo];
                        }
                        else if (MimicosRecibidos.EstadoImpresora == enmEstadoImpresora.Warning)
                        {
                            imgPrinterBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleWarning);
                            imgPrinter.Background = _imagenesPantalla[enmTipoImagen.ImpresoraWarning];
                        }
                        else if (MimicosRecibidos.EstadoImpresora == enmEstadoImpresora.PocoPapel)
                        {
                            imgPrinterBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleWarning);
                            imgPrinter.Background = _imagenesPantalla[enmTipoImagen.ImpresoraPocoPapel];
                        }
                        else if (MimicosRecibidos.EstadoImpresora == enmEstadoImpresora.CierreZ)
                        {
                            imgPrinterBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleWarning);
                            imgPrinter.Background = _imagenesPantalla[enmTipoImagen.ImpresoraCierreZ];
                        }
                    }

                    if (MimicosRecibidos.EstadoAntena != enmEstadoDispositivo.Nada)
                    {
                        if (MimicosRecibidos.EstadoAntena == enmEstadoDispositivo.OK)
                        {
                            imgAntenaBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgAntena.Background = _imagenesPantalla[enmTipoImagen.AntenaOk];
                        }
                        else if (MimicosRecibidos.EstadoAntena == enmEstadoDispositivo.Error)
                        {
                            imgAntenaBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleError);
                            imgAntena.Background = _imagenesPantalla[enmTipoImagen.AntenaError];
                        }
                        else if (MimicosRecibidos.EstadoAntena == enmEstadoDispositivo.No)
                        {
                            imgAntenaBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgAntena.Background = _imagenesPantalla[enmTipoImagen.AntenaNo];
                        }
                        else if (MimicosRecibidos.EstadoAntena == enmEstadoDispositivo.Warning)
                        {
                            imgAntenaBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleWarning);
                            imgAntena.Background = _imagenesPantalla[enmTipoImagen.AntenaWarning];
                        }
                    }

                    if (MimicosRecibidos.EstadoSeparador != enmEstadoSeparador.Nada)
                    {
                        if (MimicosRecibidos.EstadoSeparador == enmEstadoSeparador.Libre)
                        {
                            imgSeparadorBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgSeparador.Background = _imagenesPantalla[enmTipoImagen.SeparadorLibre];
                        }
                        else if (MimicosRecibidos.EstadoSeparador == enmEstadoSeparador.Ocupado)
                        {
                            imgSeparadorBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgSeparador.Background = _imagenesPantalla[enmTipoImagen.SeparadorOcupado];
                        }
                        else if (MimicosRecibidos.EstadoSeparador == enmEstadoSeparador.No)
                        {
                            imgSeparadorBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgSeparador.Background = _imagenesPantalla[enmTipoImagen.SeparadorNo];
                        }
                        else if (MimicosRecibidos.EstadoSeparador == enmEstadoSeparador.Warning)
                        {
                            imgSeparadorBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleWarning);
                            imgSeparador.Background = _imagenesPantalla[enmTipoImagen.SeparadorWarning];
                        }
                        else if (MimicosRecibidos.EstadoSeparador == enmEstadoSeparador.Error)
                        {
                            imgSeparadorBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleError);
                            imgSeparador.Background = _imagenesPantalla[enmTipoImagen.SeparadorError];
                        }
                    }

                    if (ConfigViaRecibida?.TChip == "S" && MimicosRecibidos.EstadoTarjetaChip != enmEstadoDispositivo.Nada)
                    {
                        if (MimicosRecibidos.EstadoTarjetaChip == enmEstadoDispositivo.OK)
                        {
                            imgTarjChipBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgTarjChip.Background = _imagenesPantalla[enmTipoImagen.TChipOk];
                        }
                        else if (MimicosRecibidos.EstadoTarjetaChip == enmEstadoDispositivo.Reading)
                        {
                            imgTarjChipBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleHighlighted);
                            imgTarjChip.Background = _imagenesPantalla[enmTipoImagen.TChipReading];
                        }
                        else if (MimicosRecibidos.EstadoTarjetaChip == enmEstadoDispositivo.Error)
                        {
                            imgTarjChipBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleError);
                            imgTarjChip.Background = _imagenesPantalla[enmTipoImagen.TChipError];
                        }
                        else if (MimicosRecibidos.EstadoTarjetaChip == enmEstadoDispositivo.No)
                        {
                            imgTarjChipBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgTarjChip.Background = _imagenesPantalla[enmTipoImagen.TChipNo];
                        }
                        else if (MimicosRecibidos.EstadoTarjetaChip == enmEstadoDispositivo.Warning)
                        {
                            imgTarjChipBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleWarning);
                            imgTarjChip.Background = _imagenesPantalla[enmTipoImagen.TChipWarning];
                        }
                    }

                    if (MimicosRecibidos.EstadoBarrera != enmEstadoBarrera.Nada)
                    {
                        if (MimicosRecibidos.EstadoBarrera == enmEstadoBarrera.Abajo)
                        {
                            imgBarreraBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgBarrera.Background = _imagenesPantalla[enmTipoImagen.BarreraAbajo];
                        }
                        else if (MimicosRecibidos.EstadoBarrera == enmEstadoBarrera.Arriba)
                        {
                            imgBarreraBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleHighlighted);
                            imgBarrera.Background = _imagenesPantalla[enmTipoImagen.BarreraArriba];
                        }
                        else if (MimicosRecibidos.EstadoBarrera == enmEstadoBarrera.Error)
                        {
                            imgBarreraBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleError);
                            imgBarrera.Background = _imagenesPantalla[enmTipoImagen.BarreraError];
                        }
                        else if (MimicosRecibidos.EstadoBarrera == enmEstadoBarrera.No)
                        {
                            imgBarreraBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                            imgBarrera.Background = _imagenesPantalla[enmTipoImagen.BarreraNo];
                        }
                    }

                    if (MimicosRecibidos.SemaforoMarquesina != eEstadoSemaforo.Nada)
                    {
                        imgSemMarquesinaBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                        if (MimicosRecibidos.SemaforoMarquesina == eEstadoSemaforo.Rojo) imgSemMarquesina.Background = _imagenesPantalla[enmTipoImagen.SemMarquesinaRojo];
                        else if (MimicosRecibidos.SemaforoMarquesina == eEstadoSemaforo.Verde) imgSemMarquesina.Background = _imagenesPantalla[enmTipoImagen.SemMarquesinaVerde];
                    }

                    if (MimicosRecibidos.SemaforoPaso != eEstadoSemaforo.Nada)
                    {
                        imgSemPasoBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                        if (MimicosRecibidos.SemaforoPaso == eEstadoSemaforo.Rojo)
                        {                    
                            imgSemPaso.Background = _imagenesPantalla[enmTipoImagen.SemPasoRojo];
                        }
                        else if (MimicosRecibidos.SemaforoPaso == eEstadoSemaforo.Verde)
                        {
                            imgSemPaso.Background = _imagenesPantalla[enmTipoImagen.SemPasoVerde];
                        }
                        else if (MimicosRecibidos.SemaforoPaso == eEstadoSemaforo.No)
                        {
                            imgSemPaso.Background = _imagenesPantalla[enmTipoImagen.SemPasoNo];
                        }
                    }

                    
                    if (MimicosRecibidos.CampanaViolacion == enmEstadoAlarma.Activa)
                    {
                        imgCampanaBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyleHighlighted);
                        imgCampana.Background = _imagenesPantalla[enmTipoImagen.AlarmaActiva];
                    }
                    else if(MimicosRecibidos.CampanaViolacion == enmEstadoAlarma.Ok)
                    {
                        imgCampanaBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                        imgCampana.Background = _imagenesPantalla[enmTipoImagen.AlarmaOk];
                    }
                    else
                    {
                        imgCampanaBorder.Style = Estilo.FindResource<Style>(ResourceList.BorderIconStyle);
                        imgCampana.Background = _imagenesPantalla[enmTipoImagen.AlarmaNo];
                    }
                    
                }));
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("PantallaManual:OnRecibeMimicosDispositivos() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("PantallaManual:OnRecibeMimicosDispositivos() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

        #region Metodos de recepcion y envio de datos a modulo de logica
        /// <summary>
        /// Este metodo se llama cuando se desconecta un cliente.
        /// </summary>
        public void CambioEstadoConexion()
        {
            var mensajeDesconexion = new Mensajes();
            try
            {
                //Se desconecto el cliente
                if (!AsynchronousSocketListener.PuertoAbierto())
                {
                    //Si habia alguna ventana abierta la cierro
                    if (_subVentana != null)
                        CargarSubVentana(enmSubVentana.Principal);
                }

                _bEstoyEsperandoRespuesta = false;
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    txtMensajesVia1.Text = Traduccion.Traducir("No se puede conectar con servicio de lógica.");
                    txtMensajesVia2.Text = string.Empty;
                    txtMensajesVia3.Text = string.Empty;
                }));
            }
            catch
            {

            }
        }

        /// <summary>
        /// Este metodo recibe el string JSON que llega desde el socket
        /// </summary>
        /// <param name="JsonString"></param>
        public void RecibirDatosLogica(ComandoLogica comandoJson)
        {
            try
            {
                if (comandoJson.Accion != enmAccion.FILAVEHICULOS)
                {
                    if(comandoJson.Accion != enmAccion.MENSAJES && comandoJson.Accion != enmAccion.MIMICOS)
                        _logger.Info("RecibirDatosLogica -> Accion[{0}] Status[{1}] SubVentana[{2}]", comandoJson.Accion,
                            comandoJson.CodigoStatus.ToString(), _subVentana == null ? "NULL" : "NOT NULL");
                        _logger.Trace("RecibirDatosLogica -> Accion[{0}] Status[{1}] SubVentana[{2}] Operacion[{3}]",
                        comandoJson.Accion,
                        comandoJson.CodigoStatus.ToString(),
                        _subVentana == null ? "NULL" : "NOT NULL",
                        comandoJson.Operacion);
                }

                lock (oLock)
                {
                    AnalizarComandoMenu(comandoJson);

                    /*if (_subVentana != null)
                        _subVentana.RecibirDatosLogica(comandoJson);
                    else
                        AnalizoDatosRecibidos(comandoJson);*/

                    if (_subVentana == null || _subVentana.RecibirDatosLogica(comandoJson))
                        AnalizoDatosRecibidos(comandoJson);
                }
                
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("PantallaManual:RecibirDatosLogica() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("PantallaManual:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }

        /// <summary>
        /// Este metodo envia un json formateado en string hacia el socket
        /// </summary>
        /// <param name="status"></param>
        /// <param name="Accion"></param>
        /// <param name="Operacion"></param>
        public void EnviarDatosALogica(enmStatus status, enmAccion Accion, string Operacion)
        {
            var comandoJson = new ComandoLogica(status, Accion, Operacion);

            try
            {
                AsynchronousSocketListener.SendDataToAll(comandoJson);
                SetTimeOutTimer();
            }
            catch (Exception ex)
            {
                _logger.Debug("PantallaManual:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar enviar datos a logica.");
            }
        }
        #endregion

        #region Metodos de recepcion y analisis de teclas presionadas

        public void OnLectorCodigoBarras(TextCompositionEventArgs e)
        {
            string key = e.Text;

            if (!string.IsNullOrEmpty(key))
            {
                // Lectura de código de barras
                if (!_onLecturaCB && key[0] == _caracterIncioCB[0])
                {
                    _onLecturaCB = true;
                    _codigoDeBarras = string.Empty;

                    if (_eFinLectura == eFinLecturaCB.Tiempo)
                        _timerLecturaCB.Start();
                }
                else if (_onLecturaCB)
                {
                    if (key[0] == _finLectura.ToString()[0] && _eFinLectura == eFinLecturaCB.CaracterDeFin)
                    {
                        _onLecturaCB = false;
                        ProcesarCB();
                        _codigoDeBarras = string.Empty;
                    }
                    else
                    {
                        _codigoDeBarras += key;
                    }
                }
            }
        }


        /// <summary>
        /// Evento que recibe la tecla presionada
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void OnTecla(object sender, KeyEventArgs e)
        {
            string logTecla = Teclado.GetEtiquetaTecla(e.Key, Keyboard.Modifiers);
            if (string.IsNullOrEmpty(logTecla))
            {
                if ((_subVentana is IngresoSistema || _subVentana is VentanaCambioPassword) && Teclado.GetKeyAlphaNumericValue(e.Key) != null)
                {
                    logTecla = "User/Pass char *";
                }
                else
                {
                    logTecla = Teclado.ConvertKeyToString(e.Key);
                    if (string.IsNullOrEmpty(logTecla))
                        logTecla = e.Key.ToString();
                }
            }
            _logger.Info("PantallaManual:OnTecla() [{0}]", logTecla);

            TeclasMaximaPrioridad(e.Key);

            if (!_onLecturaCB) // Lectura por teclado
            {
                if( _subVentana != null )
                    _subVentana.ProcesarTecla( e.Key );
                else
                {
                    if( _bEstoyEsperandoRespuesta == false )
                    {
                        // Windows envia F10 como System, hay que mandar SystemKey
                        if( e.Key == Key.System )
                            AnalizaTecla( Key.F10 );
                        else
                            AnalizaTecla( e.Key );
                    }
                } 
            }
        }

        public void OnTeclaUp(object sender, KeyEventArgs e)
        {
            if (_subVentana != null)
                _subVentana.ProcesarTeclaUp(e.Key);
        }

        /// <summary>
        /// Este metodo contiene las teclas que se ejecutan siempre, independientemente
        /// de la ventana o subventana visible.
        /// </summary>
        /// <param name="tecla"></param>
        private void TeclasMaximaPrioridad(Key tecla)
        {
            if (Teclado.IsFunctionKey(tecla, "SubeBarrera"))
            {
                //Envio el comando de tecla subir barrera a logica
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_SUBEBARRERA, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "BajaBarrera"))
            {
                //Envio el comando de tecla bajar barrera a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_BAJABARRERA, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Semaforo"))
            {
                //Envio el comando de tecla semaforo a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_SEMAFORO, string.Empty);
            }
        }

        /// <summary>
        /// Recibe la tecla que fue presionada y llama a la función correspondiente a ser ejecutada.
        /// <param name="tecla"></param>
        /// </summary>
        public void AnalizaTecla(Key tecla)
        {
            Vehiculo vehiculo = new Vehiculo();
            List<DatoVia> listaDV = new List<DatoVia>();

            if (Teclado.IsFunctionKey(tecla,"Turno"))
            {
                //Envio el comando de tecla turno a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_TURNO, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Menu"))
            {
                //Envio el comando de tecla menu a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_MENU, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Exento"))
            {
                //Envio el comando de tecla exento a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_EXENTO, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Foto"))
            {
                //Envio el comando de tecla foto a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_FOTO, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Patente"))
            {
                //Envio el comando de tecla foto a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_PATENTE, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "TagManual"))
            {
                //Envio el comando de tecla foto a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_TAGMANUAL, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Retiro"))
            {
                //Envio el comando de tecla retiro a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_RETIRO, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Venta"))
            {
                //Envio el comando de tecla venta a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_VENTA, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Observaciones"))
            {
                //Envio el comando de tecla observaciones a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_OBSERVACIONES, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "PagoDiferido"))
            {
                //Envio el comando de tecla pago diferido a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_PAGODIFERIDO, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "CobroDeudas"))
            {
                //Envio el comando de tecla cobro deudas a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_COBRODEUDAS, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "TicketManual"))
            {
                //Envio el comando de tecla ticket manual a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_TICKETMANUAL, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Comitiva"))
            {
                //Envio el comando de tecla ticket manual a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_COMITIVA, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "MonedaExtranjera"))
            {
                //Envio el comando de tecla moneda extranjera a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_MONEDAEXTRA, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "SimulacionPaso"))
            {
                //Envio el comando de tecla simulacion paso a logica
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_SIMULACPASO, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Quiebre"))
            {
                //Envio el comando de tecla quiebre a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_QUIEBRE, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Cash"))
            {
                //Envio el comando de tecla cash a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CASH, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Factura"))
            {
                //Envio el comando de tecla factura a logica
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_FACTURA, string.Empty);
            }
            else if (tecla == Key.F5)
            {
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_VUELTO, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "ValePrepago"))
            {
                //Envio el comando de tecla observaciones a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_VALEPREPAGO, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "AutorazacionPaso"))
            {
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_AUTORIZACIONPASO, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Enter"))
            {
                //Envio el comando de tecla enter a logica
                if(!_estoyEsperandoConfirmacion)
                    EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_ENTER, string.Empty);
                else
                {
                    _estoyEsperandoConfirmacion = false;
                    listaDV = new List<DatoVia>();

                    ClassUtiles.InsertarDatoVia(_causa, ref listaDV);

                    if (_causa.Codigo == eCausas.CambiarFormaPago)
                    {
                        ClassUtiles.InsertarDatoVia(_comando, ref listaDV);
                        _comando = null;
                    }

                    if ( _causa.Codigo == eCausas.AperturaTurno || _causa.Codigo == eCausas.AperturaTurnoMantenimiento )
                    {
                        ClassUtiles.InsertarDatoVia( ClassUtiles.ExtraerObjetoJson<Modos>( _auxString ), ref listaDV );

                        if( _causa.Codigo == eCausas.AperturaTurno )
                            ClassUtiles.InsertarDatoVia( ClassUtiles.ExtraerObjetoJson<Operador>( _auxString ), ref listaDV ); 
                    }

                    TecladoOculto();
                    _auxString = string.Empty;
                    _causa = null;
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        EnviarDatosALogica(enmStatus.Ok, enmAccion.CONFIRMAR, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        //Borro mensajes previos de las lineas y escribo la causa
                        MensajeDescripcion(string.Empty);
                        txtMensajesVia1.Text = string.Empty;
                        txtMensajesVia2.Text = string.Empty;
                        txtMensajesVia3.Text = string.Empty;
                    }));
                }
            }
            else if (Teclado.IsFunctionKey(tecla, "Escape"))
            {
                //Envio el comando de tecla enter a logica
                if (!_estoyEsperandoConfirmacion)
                    EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_ESCAPE, string.Empty);
                else
                {
                    _causa = null;
                    _estoyEsperandoConfirmacion = false;
                    //Borro mensajes previos de las lineas y escribo la causa
                    MensajeDescripcion(string.Empty);
                    txtMensajesVia1.Text = string.Empty;
                    txtMensajesVia2.Text = string.Empty;
                    txtMensajesVia3.Text = string.Empty;

                    if (!string.IsNullOrEmpty(_auxString))
                    {
                        _causa = ClassUtiles.ExtraerObjetoJson<Causa>(_auxString);
                        if(_causa.Codigo == eCausas.PagoTarjetaChip)
                            EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_ESCAPE, string.Empty);
                        else if(_causa.Codigo == eCausas.SimulacionPaso)
                        {
                            Utiles.ClassUtiles.InsertarDatoVia(_causa, ref listaDV);
                            EnviarDatosALogica(enmStatus.Abortada, enmAccion.T_ESCAPE, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        }

                        _auxString = string.Empty;
                    }
                }
            }
            else if (Teclado.IsFunctionKey(tecla, "Cancelar"))
            {
                //Envio el comando de tecla escape a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CANCELAR, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "DetracManual"))
            {
                //Envio el comando de tecla detraccion manual a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_DETRACMAN, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "TarjetaCredito"))
            {
                //Envio el comando de tecla pago tarjeta de credito a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_TARJETACREDITO, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "UnViaje"))
            {
                //Envio el comando de tecla un viaje a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_UNVIAJE, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Tarjeta"))
            {
                //Envio el comando de tecla tarjeta a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_TARJETA, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Exento1"))
            {
                //Envio el comando de tecla exento ambulancia a logica                
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_EXENTOAMBU, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Exento2"))
            {
                //Envio el comando de tecla exento bombero a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_EXENTOBOMB, string.Empty);
            }
            else if (Teclado.IsFunctionKey(tecla, "Exento3"))
            {
                //Envio el comando de tecla exento policia a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_EXENTOPOLI, string.Empty);
            }
            //Analizo las teclas de categoria
            else if (Teclado.IsFunctionKey(tecla, "Categoria1"))
            {
                vehiculo.Categoria = 1;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                CargarSubVentana(enmSubVentana.Categorias);
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria2"))
            {
                vehiculo.Categoria = 2;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria3"))
            {
                vehiculo.Categoria = 3;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria4"))
            {
                vehiculo.Categoria = 4;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria5"))
            {
                vehiculo.Categoria = 5;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria6"))
            {
                vehiculo.Categoria = 6;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria7"))
            {
                vehiculo.Categoria = 7;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria8"))
            {
                vehiculo.Categoria = 8;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria9"))
            {
                vehiculo.Categoria = 9;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria10"))
            {
                vehiculo.Categoria = 10;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria11"))
            {
                vehiculo.Categoria = 11;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria12"))
            {
                vehiculo.Categoria = 12;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria13"))
            {
                vehiculo.Categoria = 13;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria14"))
            {
                vehiculo.Categoria = 14;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria15"))
            {
                vehiculo.Categoria = 15;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria16"))
            {
                vehiculo.Categoria = 16;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria17"))
            {
                vehiculo.Categoria = 17;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria18"))
            {
                vehiculo.Categoria = 18;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria19"))
            {
                vehiculo.Categoria = 19;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "Categoria20"))
            {
                vehiculo.Categoria = 20;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (Teclado.IsFunctionKey(tecla, "FilaVehiculos"))
            {
                if (_ventanaFilaVehiculos == null && TurnoRecibido.EstadoTurno == enmEstadoTurno.Abierta)
                {
                    _ventanaFilaVehiculos = new VentanaFilaVehiculos(this);
                    _posicionSubV = Clases.Utiles.FrameworkElementPointToScreenPoint(txtMensajesVia3);
                    _ventanaFilaVehiculos.Top = _posicionSubV.Y;
                    _ventanaFilaVehiculos.Left = _posicionSubV.X - (_ventanaFilaVehiculos.Width / 2) + 10;
                    _ventanaFilaVehiculos.Show();
                    EnviarDatosALogica(enmStatus.Tecla, enmAccion.FILAVEHICULOS, "SHOW");
                }
                else if (_ventanaFilaVehiculos != null && _ventanaFilaVehiculos.TieneFoco)
                {
                    _ventanaFilaVehiculos.TieneFoco = false;
                    _ventanaFilaVehiculos.Close();
                    _ventanaFilaVehiculos = null;
                    EnviarDatosALogica(enmStatus.Tecla, enmAccion.FILAVEHICULOS, "HIDE");
                }
            }
        }

        public void AnalizaToque(Key tecla)
        {
            Vehiculo vehiculo = new Vehiculo();
            List<DatoVia> listaDV = new List<DatoVia>();

            if (tecla == Key.T)
            {
                //Envio el comando de tecla turno a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_TURNO, string.Empty);
            }
            else if (tecla == Key.M)
            {
                //Envio el comando de tecla menu a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_MENU, string.Empty);
            }
            else if (tecla == Key.X)
            {
                //Envio el comando de tecla exento a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_EXENTO, string.Empty);
            }
            else if (tecla == Key.F4)
            {
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.COMITIVA_PARCIAL, string.Empty);
            }
            else if (tecla == Key.P)
            {
                //Envio el comando de tecla foto a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_FOTO, string.Empty);
            }
            else if (tecla == Key.Z)
            {
                //Envio el comando de tecla foto a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_PATENTE, string.Empty);
            }
            else if (tecla == Key.G)
            {
                //Envio el comando de tecla placa manual a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_TAGMANUAL, string.Empty);
            }
            else if (tecla == Key.J)
            {
                //Envio el comando de tecla retiro a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_RETIRO, string.Empty);
            }
            else if (tecla == Key.V)
            {
                //Envio el comando de tecla venta a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_VENTA, string.Empty);
            }
            else if (tecla == Key.O)
            {
                //Envio el comando de tecla observaciones a logica y espero la respuesta
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_OBSERVACIONES, string.Empty);
            }
            else if (tecla == Key.S)
            {
                //Envio el comando de tecla simulacion paso a logica
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_SIMULACPASO, string.Empty);
            }
            else if (tecla == Key.Q)
            {
                //Envio el comando de tecla quiebre a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_QUIEBRE, string.Empty);
            }
            else if (tecla == Key.C)
            {
                //Envio el comando de tecla cash a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CASH, string.Empty);
            }
            if (tecla == Key.L)
            {
                //Envio el comando de tecla subir barrera a logica
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_SUBEBARRERA, string.Empty);
            }
            else if (tecla == Key.E)
            {
                //Envio el comando de tecla bajar barrera a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_BAJABARRERA, string.Empty);
            }
            else if (tecla == Key.F)
            {
                //Envio el comando de tecla semaforo a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_SEMAFORO, string.Empty);
            }
            else if (tecla == Key.F9)
            {
                //Envio el comando de tecla factura a logica
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_FACTURA, string.Empty);
            }
            else if (tecla == Key.I)
            {
                //Envio el comando de tecla pago difereido a logica
                _bEstoyEsperandoRespuesta = true;
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_PAGODIFERIDO, string.Empty);
            }
            else if (tecla == Key.Enter)
            {
                //Envio el comando de tecla enter a logica
                if (!_estoyEsperandoConfirmacion)
                    if (_subVentana != null)
                    {
                        _subVentana.ProcesarTecla(Key.Enter);
                    }
                    else
                    {
                        EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_ENTER, string.Empty);
                    }
                else
                {
                    _estoyEsperandoConfirmacion = false;
                    listaDV = new List<DatoVia>();

                    ClassUtiles.InsertarDatoVia(_causa, ref listaDV);

                    if (_causa.Codigo == eCausas.CambiarFormaPago)
                    {
                        ClassUtiles.InsertarDatoVia(_comando, ref listaDV);
                        _comando = null;
                    }

                    if (_causa.Codigo == eCausas.AperturaTurno || _causa.Codigo == eCausas.AperturaTurnoMantenimiento)
                    {
                        ClassUtiles.InsertarDatoVia(ClassUtiles.ExtraerObjetoJson<Modos>(_auxString), ref listaDV);

                        if (_causa.Codigo == eCausas.AperturaTurno)
                            ClassUtiles.InsertarDatoVia(ClassUtiles.ExtraerObjetoJson<Operador>(_auxString), ref listaDV);
                    }

                    _auxString = string.Empty;
                    _causa = null;
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        EnviarDatosALogica(enmStatus.Ok, enmAccion.CONFIRMAR, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        //Borro mensajes previos de las lineas y escribo la causa
                        MensajeDescripcion(string.Empty);
                        txtMensajesVia1.Text = string.Empty;
                        txtMensajesVia2.Text = string.Empty;
                        txtMensajesVia3.Text = string.Empty;
                    }));
                }
            }
            else if (tecla == Key.Escape)
            {
                
                {
                    _causa = null;
                    _estoyEsperandoConfirmacion = false;
                    //Borro mensajes previos de las lineas y escribo la causa
                    MensajeDescripcion(string.Empty);
                    txtMensajesVia1.Text = string.Empty;
                    txtMensajesVia2.Text = string.Empty;
                    txtMensajesVia3.Text = string.Empty;

                    if (!string.IsNullOrEmpty(_auxString))
                    {
                        _causa = ClassUtiles.ExtraerObjetoJson<Causa>(_auxString);
                        if (_causa.Codigo == eCausas.SimulacionPaso)
                        {
                            Utiles.ClassUtiles.InsertarDatoVia(_causa, ref listaDV);
                            EnviarDatosALogica(enmStatus.Abortada, enmAccion.T_ESCAPE, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        }

                        _auxString = string.Empty;
                    }
                    EnviarDatosALogica(enmStatus.Abortada, enmAccion.T_ESCAPE, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    CargarSubVentana(enmSubVentana.Categorias);
                }
            }
            else if (tecla == Key.K)
            {
                //Envio el comando de tecla escape a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CANCELAR, string.Empty);
            }
            else if (tecla == Key.B)
            {
                //Envio el comando de tecla detraccion manual a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_DETRACMAN, string.Empty);
            }
            else if (tecla == Key.N)
            {
                //Envio el comando de tecla un viaje a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_UNVIAJE, string.Empty);
            }
            else if (tecla == Key.D)
            {
                //Envio el comando de tecla tarjeta a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_TARJETACREDITO, string.Empty);
            }
            else if (tecla == Key.F1)
            {
                //Envio el comando de tecla exento ambulancia a logica                
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_EXENTOAMBU, string.Empty);
            }
            else if (tecla == Key.F2)
            {
                //Envio el comando de tecla exento bombero a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_EXENTOBOMB, string.Empty);
            }
            else if (tecla == Key.F3)
            {
                //Envio el comando de tecla exento policia a logica
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_EXENTOPOLI, string.Empty);
            }
            //Analizo las teclas de categoria
            else if (tecla == Key.D1)
            {
                vehiculo.Categoria = 1;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (tecla == Key.D2)
            {
                vehiculo.Categoria = 2;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (tecla == Key.D3)
            {
                vehiculo.Categoria = 3;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (tecla == Key.D4)
            {
                vehiculo.Categoria = 4;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (tecla == Key.D5)
            {
                vehiculo.Categoria = 5;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (tecla == Key.D6)
            {
                vehiculo.Categoria = 6;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (tecla == Key.D7)
            {
                vehiculo.Categoria = 7;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (tecla == Key.D8)
            {
                vehiculo.Categoria = 8;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (tecla == Key.D9)
            {
                vehiculo.Categoria = 9;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }
            else if (tecla == Key.D0)
            {
                vehiculo.Categoria = 10;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_CATEGORIA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            }            
        }

        private void ProcesarCB()
        {
            InfoClearing infoClearing = new InfoClearing();

            infoClearing.CodigoBarras = _codigoDeBarras;
            infoClearing.CantCaracteres = (byte)_codigoDeBarras.Length;

            List<DatoVia> listaDV = new List<DatoVia>();
            ClassUtiles.InsertarDatoVia(infoClearing, ref listaDV );
            EnviarDatosALogica( enmStatus.Tecla, enmAccion.T_AUTORIZACIONPASO, JsonConvert.SerializeObject( listaDV, jsonSerializerSettings ) );
        }
        #endregion

        #region Metodo de muestra de mensaje en cuadro de descripcion
        public void MensajeDescripcion(string mensaje, bool traducir = true, int lineaMensaje = 1)
        {
            txtMensajesVia1.Dispatcher.Invoke((Action)(() =>
            {
                if (traducir)
                {
                    switch (lineaMensaje)
                    {
                        case 1:
                            txtMensajesVia1.Text = Traduccion.Traducir(mensaje);
                            break;
                        case 2:
                            txtMensajesVia2.Text = Traduccion.Traducir(mensaje);
                            break;
                        case 3:
                            txtMensajesVia3.Text = Traduccion.Traducir(mensaje);
                            break;
                    }
                }
                else
                {
                    switch (lineaMensaje)
                    {
                        case 1:
                            txtMensajesVia1.Text = mensaje;
                            break;
                        case 2:
                            txtMensajesVia2.Text = mensaje;
                            break;
                        case 3:
                            txtMensajesVia3.Text = mensaje;
                            break;
                    }
                }
            }));
        }
        #endregion

        #region Metodo de carga de sub-ventana
        private readonly Mutex mutexSubventana = new Mutex();

        /// <summary>
        /// Metodo que carga una subventana en el border correspondiente
        /// </summary>
        /// <param name="subVentana"></param>
        public void CargarSubVentana(enmSubVentana subVentana)
        {
            _logger.Info("PantallaManual:CargarSubVentana() [{0}]", subVentana.ToString());
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                mutexSubventana.WaitOne();
                try
                {
                    _estoyEsperandoConfirmacion = false;

                    switch (subVentana)
                    {
                        case enmSubVentana.IngresoSistema:
                            {
                                if (_subVentana == null)
                                {
                                    _subVentana = new IngresoSistema(this);
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                }
                                break;
                            }
                        case enmSubVentana.MenuPrincipal:
                            {
                                _subVentana = new MenuPrincipal(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }
                        case enmSubVentana.Exento:
                            {
                                if (_subVentana == null)
                                {
                                    object objAuxiliar = ClassUtiles.ExtraerObjetoJson<PatenteExenta>(ParametroAuxiliar);
                                    _subVentana = new MenuPrincipal(this, objAuxiliar);
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                }
                                break;
                            }
                        case enmSubVentana.Foto:
                            {
                                if (_subVentana == null)
                                {
                                    _subVentana = new VentanaFoto(this);
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                }
                                break;
                            }
                        case enmSubVentana.Patente:
                            {
                                if (_subVentana == null)
                                {
                                    _subVentana = new VentanaPatente(this);
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                }
                                break;
                            }
                        case enmSubVentana.CantEjes:
                            {
                                if (_subVentana == null)
                                {
                                    _subVentana = new VentanaCantEjes(this);
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                }
                                break;
                            }
                        case enmSubVentana.Vuelto:
                            {
                                if (_subVentana == null)
                                {
                                    _subVentana = new VentanaVuelto(this);
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                }
                                break;
                            }
                        case enmSubVentana.Categorias:
                            {
                                if (_subVentana == null && txtEstadoVia.Text != "Cerrada")
                                {
                                    _subVentana = new VentanaCategorias(this);
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                    TecladoOculto();
                                }
                                break;
                            }
                        case enmSubVentana.EncuestaUsuarios:
                            {
                                if (_subVentana == null && txtEstadoVia.Text != "Cerrada")
                                {
                                    _subVentana = new VentanaEncuestaUsuarios(this);
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                    TecladoOculto();
                                }
                                break;
                            }
                        case enmSubVentana.FormaPago:
                            {
                                if (_subVentana == null)
                                {
                                    _subVentana = new VentanaFormaPago(this);
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                    TecladoOculto();
                                }
                                break;
                            }
                        case enmSubVentana.TagOcrManual:
                            {
                                _subVentana = new VentanaTagManual(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }
                        case enmSubVentana.FondoCambio:
                            {
                                if (_subVentana == null)
                                {
                                    _subVentana = new VentanaFondoCambio(this);
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                }
                                break;
                            }
                        case enmSubVentana.RetiroAnticipado:
                            {
                                if (_subVentana == null)
                                {
                                    if (ClassUtiles.ExtraerObjetoJson<RetiroAnticipado>(ParametroAuxiliar).PorDenominacion)
                                    {
                                        _subVentana = new VentanaRetiroAnticipadoDenominaciones(this);
                                    }
                                    else
                                    {
                                        _subVentana = new VentanaRetiroAnticipado(this);
                                    }
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                }
                                break;
                            }
                        case enmSubVentana.Liquidacion:
                            {
                                if (_subVentana == null)
                                {
                                    _subVentana = new VentanaLiquidacion(this);
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                }
                                break;
                            }
                        case enmSubVentana.CambioPassword:
                            {
                                _subVentana = new VentanaCambioPassword(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }
                        case enmSubVentana.Venta:
                            {
                                if (_subVentana == null)
                                {
                                    _subVentana = new VentanaRecarga(this);
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                }
                                break;
                            }
                        case enmSubVentana.CrearPagoDiferido:
                            {
                                _subVentana = new VentanaPagoDiferido(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }
                        case enmSubVentana.CobroDeudas:
                            {
                                _subVentana = new VentanaCobroDeudas(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }
                        /*case enmSubVentana.TicketManual:
                            {
                                _subVentana = new VentanaTicketManual(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }*/
                        case enmSubVentana.TicketManualComitiva:
                            {
                                _subVentana = new VentanaTicketManualComitiva(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }
                        case enmSubVentana.Recorridos:
                            {
                                _subVentana = new VentanaRecorridos(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }
                        case enmSubVentana.MonedaExtranjera:
                            {
                                _subVentana = new VentanaMonedaExtranjera(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }
                        case enmSubVentana.Factura:
                            {
                                if (_subVentana == null)
                                {
                                    _subVentana = new VentanaCobroFactura(this);
                                    SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                }
                                break;
                            }
                        case enmSubVentana.VentanaConfirmacion:
                            {
                                _subVentana = new VentanaConfirmacion(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }
                        case enmSubVentana.AutorizacionNumeracion:
                            {
                                _subVentana = new VentanaAutNumeracion(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }
                        case enmSubVentana.ViaEstacion:
                            {
                                _subVentana = new IngresoViaEstacion(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }
                        case enmSubVentana.AutorizacionPasoVale:
                            {
                                _subVentana = new VentanaValePrepago(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }
                        case enmSubVentana.Versiones:
                            {
                                _subVentana = new VentanaVersiones(this);
                                SubVentana.Child = _subVentana.ObtenerControlPrincipal();
                                break;
                            }
                        case enmSubVentana.Principal:
                            {
                                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                                {
                                    SubVentana.Child = null;
                                    _subVentana = null;
                                }));
                                break;
                            }
                    }
                }
                catch (Exception e)
                {
                    _logger.Warn(e.ToString());
                }
                finally
                {
                    mutexSubventana.ReleaseMutex();
                }
            }));
        }
        #endregion

        #region Timer de timeout de lectura CB
        private void OnFinLecturaCB( object sender, object e )
        {
            _onLecturaCB = false;
            _timerLecturaCB.Stop();
            ProcesarCB();
            _codigoDeBarras = string.Empty;
        }
        #endregion

        #region Timer de timeout de comunicaciones
        private void SetTimeOutTimer()
        {
            // Create a timer with a two second interval.
            timeOutTimer = new System.Timers.Timer(comTimeOut);
            // Hook up the Elapsed event for the timer. 
            timeOutTimer.Elapsed += OnTimedEvent;
            timeOutTimer.AutoReset = false;
            timeOutTimer.Enabled = true;
        }

        private void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            if(_bEstoyEsperandoRespuesta)
            {
                _bEstoyEsperandoRespuesta = false;
                timeOutTimer.Enabled = false;
            }
        }

        private void OnBorraMsgSup( object source, ElapsedEventArgs e )
        {
            for( int i = listaMensajes.Count - 1; i >= 0; i-- )
            {
                if( ( DateTime.Now - listaMensajes[i].HoraMensaje ).TotalHours > 24 )
                {
                    listaMensajes.Remove( listaMensajes[i] );
                    ListBoxMsgSupervision.Dispatcher.Invoke((Action)(() =>
                    {
                        ListBoxMsgSupervision.Items.RemoveAt(ListBoxMsgSupervision.Items.Count - 1);
                    }));
                }
            }
        }
        #endregion

        #region Metodo de actualizacion de fecha/hora mediante timer
        private void OnTick(object sender, object e)
        {
            _timerActualizaReloj.Stop();
            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                txtFecha.Text = DateTime.Now.ToString("dd/MM/yyyy");
                txtHora.Text = DateTime.Now.ToString("HH:mm:ss");
            }));
            _timerActualizaReloj.Start();
        }
        #endregion    

        #region Metodo que carga las imagenes que estan en la carpeta de recursos en RAM
        /// <summary>
        /// Carga las imagenes como brushes en un diccionario
        /// </summary>
        /// <param name="carpetaRecursos"></param>
        /// <returns></returns>
        public IDictionary<enmTipoImagen, ImageBrush> CargarImagenesRecursos(string carpetaRecursos)
        {
            IDictionary<enmTipoImagen, ImageBrush> dict = new Dictionary<enmTipoImagen, ImageBrush>();
            var imagenBrush = new ImageBrush();
            var filters = new string[] { "png" }; 

            try
            {
                foreach(enmTipoImagen key in Enum.GetValues(typeof(enmTipoImagen)))
                {
                    dict.Add(key, null);
                }

                var files = Utiles.ClassUtiles.GetFilesFrom(carpetaRecursos, filters, false);
                foreach (var imagen in files)
                {
                    //Compruebo que el recurso forme parte del enumerado para agregarlo al diccionario
                    if (Enum.IsDefined(typeof(enmTipoImagen), Path.GetFileNameWithoutExtension(imagen)))
                    {
                        imagenBrush = new ImageBrush();
                        imagenBrush.ImageSource = new BitmapImage(new Uri(BaseUriHelper.GetBaseUri(this), imagen));
                        imagenBrush.Stretch = Stretch.Uniform;
                        imagenBrush.Freeze();
                        dict[(enmTipoImagen)Enum.Parse(typeof(enmTipoImagen), Path.GetFileNameWithoutExtension(imagen))] = imagenBrush;
                        //imagenBrush = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("PantallaManual:CargarImagenesRecursos() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al cargar las imagenes desde la carpeta.");
            }
            return dict;
        }
        #endregion

        #region Metodo que devuelve una imagen de categoria de acuerdo al Vehiculo
        /// <summary>
        /// Devuelve un ImageBrush con la imagen de la categoria del vehiculo
        /// </summary>
        /// <param name="veh"></param>
        /// <returns></returns>
        private ImageBrush GetImagenCategoria(Vehiculo veh)
        {
            ImageBrush imgBrush = null;

            try
            {
                switch (veh.Categoria)
                {
                    case 0:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria0];
                            break;
                        }
                    case 1:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria1];
                            break;
                        }
                    case 2:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria2];
                            break;
                        }
                    case 3:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria3];
                            break;
                        }
                    case 4:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria4];
                            break;
                        }
                    case 5:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria5];
                            break;
                        }
                    case 6:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria6];
                            break;
                        }
                    case 7:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria7];
                            break;
                        }
                    case 8:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria8];
                            break;
                        }
                    case 9:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria9];
                            break;
                        }
                    case 10:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria10];
                            break;
                        }
                    case 11:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria11];
                            break;
                        }
                    case 12:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria12];
                            break;
                        }
                    case 13:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria13];
                            break;
                        }
                    case 14:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria14];
                            break;
                        }
                    case 15:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria15];
                            break;
                        }
                    case 16:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria16];
                            break;
                        }
                    case 17:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria17];
                            break;
                        }
                    case 18:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria18];
                            break;
                        }
                    case 19:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria19];
                            break;
                        }
                    case 20:
                        {
                            imgBrush = _imagenesPantalla[enmTipoImagen.categoria20];
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("PantallaManual:GetImagenCategoria() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al mostrar la imagen correspondiente.");
            }
            return imgBrush;
        }
        #endregion

        private void gridPrincipal_Loaded(object sender, RoutedEventArgs e)
        {
            ActualizarVersiones();
        }

        private void ActualizarVersiones()
        {
            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                var linkTimeLocal = Assembly.GetExecutingAssembly().GetLinkerTime();
                string version = Assembly.GetExecutingAssembly().GetName().Version.ToString() + " - " + linkTimeLocal.ToString("dd/MM/yyyy");
                EventoVersion versionLogica = ClassUtiles.GetProductVersion(ClassUtiles.LeerConfiguracion("Idioma", "PATH_CONFIG_LOGICA").Replace(".config", ""), "");
                txtVersionPantalla.Text = "TCP-TOLL: " + version;
                version = versionLogica.GetVersionString + " - " + versionLogica.FechaModif.ToString("dd/MM/yyyy");
                txtVersionLogica.Text = "Lógica: " + version;
            }));
        }

        #region Pantalla Tactil

        private void CANCEL_Click(object sender, RoutedEventArgs e)
        {
            CargarSubVentana(enmSubVentana.Principal);
            TecladoOculto();
            AnalizaToque(Key.K);
        }
        private void CATEGOS_Click(object sender, RoutedEventArgs e)
        {
            CargarSubVentana(enmSubVentana.Principal);
            TecladoOculto();
            AnalizaToque(Key.F4);
        }
        private void SEMAFORO_Click(object sender, RoutedEventArgs e)
        {
            AnalizaToque(Key.F);
        }
        private void SUBE_Click(object sender, RoutedEventArgs e)
        {
            CargarSubVentana(enmSubVentana.Principal);
            AnalizaToque(Key.L);
        }
        private void BAJA_Click(object sender, RoutedEventArgs e)
        {
            CargarSubVentana(enmSubVentana.Principal);
            AnalizaToque(Key.E);
        }
        private void TURNO_Click(object sender, RoutedEventArgs e)
        {
            CargarSubVentana(enmSubVentana.Principal);
            AnalizaToque(Key.T);
        }
        private void ENTERBOTON_Click(object sender, RoutedEventArgs e)
        {
            if (_estoyEsperandoConfirmacion)
                AnalizaTecla(Key.Enter);
            else if (_subVentana != null)
                AnalizaToque(Key.Enter);
        }

        
        private void TECLADO_Click(object sender, RoutedEventArgs e)
        { 
            if (_teclado)
            {
                TecladoOculto();
                _teclado = !_teclado;
            }
            else
            {
                TecladoVisible();
                _teclado = !_teclado;
            }
        }

        private void ALARMA_Click(object sender, RoutedEventArgs e)
        {
            EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_ALARMA, _alarma ? "Apagar" : "Encender" );
            _alarma = !_alarma;
        }

        #endregion

        #region Teclado

        public void TecladoVisible()
        {
            SubVentanaTeclado.Visibility = Visibility.Visible;
        }

        public void TecladoOculto()
        {
            SubVentanaTeclado.Visibility = Visibility.Hidden;
        }

        private void OCULTAR_Click(object sender, RoutedEventArgs e)
        {
            TecladoOculto();
        }
        private void Q_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.Q);
        }
        private void W_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.W);
        }
        private void E_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.E);
        }
        private void R_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.R);
        }
        private void T_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.T);
        }
        private void Y_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.Y);
        }
        private void U_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.U);
        }
        private void I_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.I);
        }
        private void O_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.O);
        }
        private void P_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.P);
        }
        private void A_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.A);
        }
        private void S_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.S);
        }
        private void D_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.D);
        }
        private void F_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.F);
        }
        private void G_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.G);
        }
        private void H_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.H);
        }
        private void J_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.J);
        }
        private void K_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.K);
        }
        private void L_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.L);
        }
        private void Ñ_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.F3);
        }
        private void Z_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.Z);
        }
        private void X_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.X);
        }
        private void C_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.C);
        }
        private void V_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.V);
        }
        private void B_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.B);
        }
        private void N_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.N);
        }
        private void M_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.M);
        }

        private void NUM0_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.D0);
        }
        private void NUM1_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.D1);
        }
        private void NUM2_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.D2);
        }
        private void NUM3_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.D3);
        }
        private void NUM4_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.D4);
        }
        private void NUM5_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.D5);
        }
        private void NUM6_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.D6);
        }
        private void NUM7_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.D7);
        }
        private void NUM8_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.D8);
        }
        private void NUM9_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.D9);
        }
        private void PUNTO_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.OemPeriod);
        }
        private void ENTER_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.Enter);
        }
        private void BACKSPACE_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.Back);
        }
        private void COMA_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.OemComma);
        }
        private void GUION_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.OemMinus);
        }
        private void ESPACIO_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.Space);
        }

        private void AND_Click(object sender, RoutedEventArgs e)
        {
            _subVentana?.ProcesarTecla(Key.Oem5);
        }

        #endregion

        private void CorrerProceso( eProcesos proceso )
        {
            ProcessStartInfo info = new ProcessStartInfo();
            _logger.Info($"CorrerProceso -> [{proceso.ToString()}]");

            switch ( proceso )
            {
                case eProcesos.Testeo:
                    info.FileName = ConfigurationManager.AppSettings["PATH_TESTEO"].ToString();

                    if( string.IsNullOrEmpty( info.FileName ) )
                        info.FileName = @"C:\Testeo\Testeo-TCI.exe";

                    info.Arguments = "Tecnico";
                    break;

                case eProcesos.Apagado:
                    info.FileName = "ShutDown";
                    info.Arguments = "/s /t 0";
                    break;

                case eProcesos.Reinicio:
                    info.FileName = "ShutDown";
                    info.Arguments = "/r /t 0";
                    break;

                case eProcesos.CierreSesion:
                    info.FileName = "ShutDown";
                    info.Arguments = "/l";
                    break;

                default:
                    break;
            }


            //Borro mensajes previos de las lineas y escribo la causa
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                txtMensajesVia1.Text = string.Empty;
                txtMensajesVia2.Text = string.Empty;
                txtMensajesVia3.Text = string.Empty;
            }));

            try
            {
                if (proceso == eProcesos.Testeo && !File.Exists(info.FileName))
                    _logger.Info($"CorrerProceso No se encontro el archivo [{info.FileName}]");
                else
                {
                    info.WorkingDirectory = Path.GetDirectoryName(info.FileName);
                    info.CreateNoWindow = true;
                    info.UseShellExecute = false;
                    Process exeProcess = Process.Start(info);

                    if (proceso == eProcesos.Testeo)
                    {
                        if (Process.GetProcesses().Any(x => x.Id == exeProcess.Id))
                        {
                            // Se debe cerrar la pantalla cuando se abre el testeo
                            Application.Current.Dispatcher.Invoke((Action)(() =>
                            {
                                _principal.CierreSubventanasAbiertas();
                                _principal.CierreThreadServidor();
                                Application.Current.Shutdown();
                                Process.GetCurrentProcess().Kill();
                            }));
                        }
                    }
                }
            }
            catch( Exception ex )
            {
                _logger.Error($"CorrerProceso [{proceso.ToString()}] -> " + ex );
            }
        }

        
    }
}
