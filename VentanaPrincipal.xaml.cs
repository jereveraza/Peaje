using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using ModuloPantallaTeclado.Clases;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Pantallas;
using System.Threading;
using System.Configuration;
using ModuloPantallaTeclado.Sub_Ventanas;
using System.Linq;
using Utiles;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Timers;

namespace ModuloPantallaTeclado
{
    /// <summary>
    /// Lógica de interacción para VentanaPrincipal.xaml
    /// </summary>
    public partial class VentanaPrincipal : Window
    {
        #region Variables de clase
        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x80000;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private Thread ThreadingServer = null;
        private IPantalla _pantallaVia = null;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");

        private System.Timers.Timer timerSetearFoco = new System.Timers.Timer();
        #endregion

        #region Constructor de la clase
        public VentanaPrincipal()
        {
            //Compruebo que esta sea la unica instancia de ejecucion corriendo actualmente.
            if (System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly().Location)).Count() > 1)
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            InitializeComponent();
            var linkTimeLocal = Assembly.GetExecutingAssembly().GetLinkerTime();
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString() + " - " + linkTimeLocal.ToString("dd/MM/yyyy");
            ventanaPrincipal.Title = Assembly.GetExecutingAssembly().GetName().Name + " " + version;

            _logger.Info("************** Inicio Pantalla TCI [{0}] **************",version);
            //Clases.Utiles.CargarFiltroPatentes();
            Clases.Utiles.CargarConfigCortarPatente();
            //Procesamiento del lector de código de barras
            TextCompositionManager.AddTextInputHandler(this, new TextCompositionEventHandler(gridPrincipal_OnTextComposition));
        }
        #endregion

        #region Hilo de servidor TCP asincronico
        public void IniciarSocketServidor()
        {
            if (ThreadingServer == null)
            {
                ThreadingServer = new Thread(StartServer);
            }
        }

        private static void StartServer()
        {
            AsynchronousSocketListener.StartListening();
        }
        #endregion

        #region Eventos de la ventana
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
#if !DEBUG
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);
#endif

            enmModeloVia modeloVia = enmModeloVia.MANUAL;
            //Dibujo la pantalla que corresponda segun el tipo de via
            try
            {
                string strModeloVia = ConfigurationManager.AppSettings["ModeloVia"];
                if (Enum.TryParse<enmModeloVia>(strModeloVia.ToUpper(), out modeloVia))
                {
                    if (modeloVia == enmModeloVia.AVI)
                        _pantallaVia = new PantallaAVI(this);
                    else if (modeloVia == enmModeloVia.DINAMICA)
                        _pantallaVia = new PantallaDinamica(this);
                    else
                        _pantallaVia = new PantallaManual(this);
                }
                else
                    _pantallaVia = new PantallaManual(this);

                borderPrincipal.Child = _pantallaVia.ObtenerControlPrincipal();
                //Inicio el hilo del servidor
                ThreadingServer.Start();
            }
            catch (Exception ex)
            {
                _logger.Fatal("VentanaPrincipal:Window_Loaded() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al instanciar la pantalla.");
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            CierreSubventanasAbiertas();
            _pantallaVia.Dispose();
#if !DEBUG
            e.Cancel = true;
#else
            CierreThreadServidor();
#endif
        }

        public void CierreSubventanasAbiertas()
        {
            AgregarSimbolo.CerrarVentanaSimbolo();
            VentanaFilaVehiculos.CerrarVentanaFilaVehiculos();
        }

        public void CierreThreadServidor()
        {
            AsynchronousSocketListener.Cerrar();
            ThreadingServer = null;
            _logger.Info("****************** Cierre Pantalla TCI ******************");
        }

        private void gridPrincipal_OnTextComposition(object sender, TextCompositionEventArgs e)
        {
            _pantallaVia.OnLectorCodigoBarras(e);
        }

        private void gridPrincipal_KeyDown(object sender, KeyEventArgs e)
        {

        }

        private void gridPrincipal_KeyUp(object sender, KeyEventArgs e)
        {

        }

        private void MainWindow_OnDeactivated(object sender, EventArgs eventArgs)
        {

        }

        public void SetWindowFocus()
        {
#if !DEBUG
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                this.Topmost = true;
                this.Focus();
                this.Activate();
                Keyboard.Focus(this);
            }));
#endif
        }

        public void SetVentanaMaximizada()
        {
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (ventanaPrincipal.WindowStyle == WindowStyle.None)
                {
                    ventanaPrincipal.WindowStyle = WindowStyle.SingleBorderWindow;
                    timerSetearFoco.Stop();
                }
                else
                {
                    ventanaPrincipal.WindowStyle = WindowStyle.None;
                    Application.Current.MainWindow.WindowState = WindowState.Maximized;
                    SetTimerFoco();
                }
            }));
        }

        private void SetTimerFoco()
        {
            if (!timerSetearFoco.Enabled)
            {
                // Create a timer with a two second interval.
                timerSetearFoco = new System.Timers.Timer();
                // Hook up the Elapsed event for the timer. 
                timerSetearFoco.Interval = 1000 * 10;
                timerSetearFoco.Elapsed -= OnTimedEvent;
                timerSetearFoco.Elapsed += OnTimedEvent;
                timerSetearFoco.AutoReset = true;
                timerSetearFoco.Enabled = true;
            }
        }

        private void OnTimedEvent(object source, ElapsedEventArgs e)
        {
            SetWindowFocus();
        }
        #endregion

    }
}
