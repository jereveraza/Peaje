using System;
using System.Windows;
using System.Windows.Input;
using ModuloPantallaTeclado.Clases;
using ModuloPantallaTeclado.Interfaces;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Windows.Media;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Entidades.Comunicacion;
using Entidades.Logica;
using Entidades;
using Utiles;
using Entidades.ComunicacionBaseDatos;

namespace ModuloPantallaTeclado.Pantallas
{
    /// <summary>
    /// Lógica de interacción para PantallaAVI.xaml
    /// </summary>
    public partial class PantallaAVI : Window, IPantalla
    {
        private VentanaPrincipal _principal;
        private ISubVentana _subVentana = null;
        public string ParametroAuxiliar { set; get; }
        public Vehiculo VehiculoRecibido { set; get; }
        public Parte ParteRecibido { set; get; }
        public Turno TurnoRecibido { set; get; }
        public Numeracion NumeracionRecibida { set; get; }
        public void OnLectorCodigoBarras(TextCompositionEventArgs e)
        {
            
        }

        public InformacionVia InformacionViaRecibida
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public ConfigVia ConfigViaRecibida
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public void TecladoVisible()
        {

        }
        public void TecladoOculto()
        {

        }
        public void ActualizarMensajes(Mensajes mensajes)
        {
           


        }
        public PantallaAVI(VentanaPrincipal principal)
        {
            InitializeComponent();
            _principal = principal;
        }

        event Action<TextCompositionEventArgs> IPantalla.LectorCodigoBarrasEvent
        {
            add
            {
                throw new NotImplementedException();
            }

            remove
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Este metodo se llama cuando se desconecta un cliente.
        /// </summary>
        public void CambioEstadoConexion()
        {

        }

        public void RecibirDatosLogica(ComandoLogica comandoJson)
        {

        }

        /// <summary>
        /// Este metodo envia un json formateado en string hacia el socket
        /// </summary>
        /// <param name="status"></param>
        /// <param name="Accion"></param>
        /// <param name="Operacion"></param>
        public void EnviarDatosALogica(enmStatus status, enmAccion Accion, string Operacion)
        {
            var comandoJson = new ComandoLogica(status,Accion, Operacion);

            try
            {
                AsynchronousSocketListener.SendDataToAll(comandoJson);
            }
            catch
            {

            }
        }

        #region Metodo de muestra de mensaje en cuadro de descripcion
        public void MensajeDescripcion(string mensaje)
        {
            //txtDescripcion.Text = mensaje;
        }
        #endregion

        public FrameworkElement ObtenerControlPrincipal()
        {
            FrameworkElement control = (FrameworkElement)borderAvi.Child;
            borderAvi.Child = null;
            Close();
            return control;
        }

        public void OnTeclaUp(object sender, KeyEventArgs e)
        {

        }

        public void OnTecla(object sender, KeyEventArgs e)// TextCompositionEventArgs e)
        {
           
        }

        /// <summary>
        /// Recibe la tecla que fue presionada y llama a la función correspondiente a ser ejecutada.
        /// <param name="tecla"></param>
        /// </summary>
        /// 
        public void AnalizaTecla(Key tecla)
        {

        }

        public void AnalizaToque(Key tecla)
        {

        }

        public void CargarSubVentana(enmSubVentana subVentana)
        {

        }

      

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
            var filters = new string[] { "png", "gif", "jpg", "bmp" };

            try
            {
                var files = Utiles.ClassUtiles.GetFilesFrom(carpetaRecursos, filters, false);
                foreach (var imagen in files)
                {
                    //Compruebo que el recurso forme parte del enumerado para agregarlo al diccionario
                    if (Enum.IsDefined(typeof(enmTipoImagen), Path.GetFileNameWithoutExtension(imagen)))
                    {
                        imagenBrush.ImageSource = new BitmapImage(new Uri(BaseUriHelper.GetBaseUri(this), imagen));
                        dict.Add((enmTipoImagen)Enum.Parse(typeof(enmTipoImagen), Path.GetFileNameWithoutExtension(imagen)), imagenBrush);
                    }
                }
            }
            catch
            {

            }
            return dict;
        }

        public void MensajeDescripcion(string mensaje, bool traducir = true, int lineaMensaje = 1)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
