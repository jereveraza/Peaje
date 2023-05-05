using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Clases;
using Entidades.Comunicacion;
using Entidades;
using System.Collections.Generic;
using Entidades.ComunicacionFoto;
using Newtonsoft.Json;
using System.IO;
using System.Timers;
using System.Windows.Input;
using System.Windows.Controls;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    /// <summary>
    /// Lógica de interacción para VentanaFoto.xaml
    /// </summary>
    public partial class VentanaFoto : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private bool _bSolicitudNuevaFoto = false;
        private bool _bZoomFoto = false;
        private string _strPathFoto;
        private Timer _timerFoto = new Timer();
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgSeleccionOpciones = "Confirme la foto";
        #endregion

        #region Constructor de la clase
        public VentanaFoto(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            SetTextoBotonesAceptarCancelar("Enter", "Escape");
            Clases.Utiles.TraducirControles<TextBlock>(Grid_Principal);
            _bSolicitudNuevaFoto = false;
            Foto foto = Utiles.ClassUtiles.ExtraerObjetoJson<Foto>( _pantalla.ParametroAuxiliar );
            _strPathFoto = Path.Combine( foto.PathFoto, foto.Nombre );

            _timerFoto.Elapsed += new ElapsedEventHandler( ChequeaExisteFoto );
            _timerFoto.Interval = 50;
            _timerFoto.AutoReset = true;
            _timerFoto.Start();
        }
        #endregion

        #region Timer de comprobacion de existencia de foto
        /// <summary>
        /// Timer que chequea si existe el archivo flg de la foto correspondiente y la actualiza en pantalla
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private void ChequeaExisteFoto( object source, ElapsedEventArgs e )
        {
            if( File.Exists( _strPathFoto + ".flg" ) )
            {
                try
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                  {
                      panelFoto.Children.Clear();
                      panelFoto.Children.Add(Clases.Utiles.CargarFotoRectangulo(_strPathFoto, Datos.AnchoFoto, Datos.AltoFoto, false, _logger));
                      _pantalla.MensajeDescripcion(msgSeleccionOpciones);
                  }));
                    _timerFoto.Stop();
                }
                catch (Exception ex)
                {
                    _logger.Debug("VentanaFoto:ChequeaExisteFoto() Exception: {0}", ex.Message.ToString());
                    _logger.Warn("ChequeaExisteFoto: Error al recibir el path de la foto de logica.");
                }
            }
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
            FrameworkElement control = (FrameworkElement)borderVentanaFoto.Child;
            borderVentanaFoto.Child = null;
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
                if (comandoJson.Accion == enmAccion.FOTO && comandoJson.Operacion != string.Empty && _bSolicitudNuevaFoto)
                {
                    Foto foto = Utiles.ClassUtiles.ExtraerObjetoJson<Foto>(comandoJson.Operacion);
                    _strPathFoto = Path.Combine(foto.PathFoto, foto.Nombre);

                    _timerFoto.Start();
                }
                else
                    bRet = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaFoto:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al recibir el path de la foto de logica.");
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
                _logger.Debug("VentanaFoto:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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
                _pantalla.CargarSubVentana(enmSubVentana.Categorias);
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                btnAceptar.Dispatcher.Invoke((Action)(() =>
                {
                    btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyleHighlighted);
                }));
            }

            List<DatoVia> listaDV = new List<DatoVia>();
            if (Teclado.IsConfirmationKey(tecla))
            {
                //Envio a logica el path de la foto a enviar con el transito
                Foto foto = new Foto();
                foto.PathFoto = _strPathFoto;
                if (_timerFoto.Enabled)
                    _timerFoto.Stop();
                Utiles.ClassUtiles.InsertarDatoVia(foto, ref listaDV);
                EnviarDatosALogica(enmStatus.Ok, enmAccion.FOTO_TRAN, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                _pantalla.MensajeDescripcion(string.Empty);
                _pantalla.CargarSubVentana(enmSubVentana.Principal);
            }
            else if (Teclado.IsFunctionKey(tecla, "Foto"))
            {
                if( !Teclado.IsExistingKey( "ZoomFoto" ) )
                {
                    _bZoomFoto = !_bZoomFoto;

                    if( _bZoomFoto )
                    {
                        //Inserto la foto con zoom en lugar de la anterior
                        Application.Current.Dispatcher.Invoke( (Action)( () =>
                        {
                            if(panelFoto.Children.Count > 0)
                                panelFoto.Children.RemoveAt( 0 );
                            panelFoto.Children.Insert( 0, Clases.Utiles.CargarFotoRectangulo( _strPathFoto, Datos.AnchoFoto, Datos.AltoFoto, _bZoomFoto, _logger ) );
                        }));
                    }
                    else
                    {
                        //Solicito una nueva foto a logica
                        SolicitudNuevaFoto nuevaFoto = new SolicitudNuevaFoto(true);
                        EnviarDatosALogica( enmStatus.Tecla, enmAccion.T_FOTO, JsonConvert.SerializeObject(nuevaFoto, jsonSerializerSettings));
                        _bSolicitudNuevaFoto = true;
                    }
                }
                else
                {
                    //Solicito una nueva foto a logica
                    SolicitudNuevaFoto nuevaFoto = new SolicitudNuevaFoto(true);
                    EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_FOTO, JsonConvert.SerializeObject(nuevaFoto, jsonSerializerSettings));
                    _bSolicitudNuevaFoto = true;
                }
            }
            else if (Teclado.IsFunctionKey(tecla, "ZoomFoto"))
            {
                //Inserto la foto con zoom en lugar de la anterior
                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    if (_bZoomFoto) _bZoomFoto = false;
                    else _bZoomFoto = true;
                    panelFoto.Children.Clear();
                    panelFoto.Children.Add( Clases.Utiles.CargarFotoRectangulo(_strPathFoto, Datos.AnchoFoto, Datos.AltoFoto, _bZoomFoto, _logger));
                }));
            }
            else if (Teclado.IsEscapeKey(tecla))
            {
                _pantalla.MensajeDescripcion(string.Empty);
                _pantalla.CargarSubVentana(enmSubVentana.Principal);
                if (_timerFoto.Enabled)
                    _timerFoto.Stop();
                EnviarDatosALogica(enmStatus.Abortada, enmAccion.FOTO_TRAN, string.Empty);
                _pantalla.CargarSubVentana(enmSubVentana.Categorias);
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
