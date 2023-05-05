using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Clases;
using Entidades.Comunicacion;
using Entidades;
using Entidades.Logica;
using System.Collections.Generic;
using Entidades.ComunicacionFoto;
using Newtonsoft.Json;
using System.IO;
using System.Timers;
using System.Windows.Input;
using System.Windows.Controls;
using Utiles.Utiles;
using System.Diagnostics;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    /// <summary>
    /// Lógica de interacción para VentanaPatente.xaml
    /// </summary>
    public partial class VentanaPatente : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private string _patenteIngresada;
        private bool _bSolicitudNuevaFoto = true;
        private bool _bZoomFoto = false;
        private string _strPathFoto = null;
        private Timer _timerFoto = new Timer();
        private Causa _causa = null;
        private AgregarSimbolo _ventanaSimbolo;
        private Point _posicionSubV;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        private const byte _maximoCaracteresPatente = 6;
        #endregion

        #region Mensajes de descripcion
        const string msgSeleccionOpciones = "Ingrese la patente";
        const string msgFormatoPatenteErr = "Formato de patente incorrecto";
        #endregion

        #region Constructor de la clase
        public VentanaPatente(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
            if (padre.VehiculoRecibido != null)
            {
                txtPatente.Text = padre.VehiculoRecibido.Patente;
            }
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            SetTextoBotonesAceptarCancelar("Enter", "Escape");
            Clases.Utiles.TraducirControles<TextBlock>(Grid_Principal);
            if (_pantalla.ParametroAuxiliar != string.Empty)
            {
                _causa = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(_pantalla.ParametroAuxiliar);

                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    lblMenuPatente.Text = _causa.Descripcion;
                    txtPatente.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                    if(_pantalla.ConfigViaRecibida.TieneOCR == 'S')
                    {
                        lblPatenteOCR.Visibility = Visibility.Visible;
                        txtPatenteOCR.Visibility = Visibility.Visible;
                    }
                }));

                Vehiculo vehiculo = Utiles.ClassUtiles.ExtraerObjetoJson<Vehiculo>(_pantalla.ParametroAuxiliar);
                if (vehiculo != null && vehiculo.InfoOCRDelantero != null && 
                    !string.IsNullOrEmpty(vehiculo.InfoOCRDelantero.Patente))
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        txtPatenteOCR.Text = vehiculo.InfoOCRDelantero.Patente;
                        txtPatente.Text = string.IsNullOrEmpty(vehiculo.Patente) ? vehiculo.InfoOCRDelantero.Patente : vehiculo.Patente;
                    }));
                }
                else
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        txtPatenteOCR.Text = "";
                        txtPatente.Text = "";
                    }));
                }
            }
            panelFoto.Children.RemoveAt(0);
            panelFoto.Children.Insert(0, Clases.Utiles.CargarFotoRectangulo("", Datos.AnchoFoto, Datos.AltoFoto, false, _logger));
            _pantalla.MensajeDescripcion(msgSeleccionOpciones);
            _pantalla.TecladoVisible();

            _timerFoto.Elapsed += new ElapsedEventHandler( ChequeaExisteFoto );
            _timerFoto.Interval = 50;
            _timerFoto.AutoReset = true;
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
            FrameworkElement control = (FrameworkElement)borderVentanaPatente.Child;
            borderVentanaPatente.Child = null;
            Close();
            return control;
        }
        #endregion

        #region Timer de comprobacion de foto
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
                      if (panelFoto.Children.Count > 0)
                          panelFoto.Children.RemoveAt(0);
                      panelFoto.Children.Insert(0, Clases.Utiles.CargarFotoRectangulo(_strPathFoto, Datos.AnchoFoto, Datos.AltoFoto, false, _logger));
                      _pantalla.MensajeDescripcion(msgSeleccionOpciones);
                  }));
                    _timerFoto.Stop();
                }
                catch (Exception ex)
                {
                    _logger.Debug("VentanaPatente:ChequeaExisteFoto() Exception: {0}", ex.Message.ToString());
                    _logger.Warn("ChequeaExisteFoto: Error al recibir el path de la foto de logica.");
                }
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
                if (comandoJson.Accion == enmAccion.FOTO && comandoJson.Operacion != string.Empty && _bSolicitudNuevaFoto)
                {
                    //Inserto la nueva foto en lugar de la anterior
                    panelFoto.Dispatcher.Invoke((Action)(() =>
                    {
                        Foto foto = Utiles.ClassUtiles.ExtraerObjetoJson<Foto>(comandoJson.Operacion);

                        _strPathFoto = Path.Combine(foto?.PathFoto, foto?.Nombre);
                        _timerFoto.Start();
                        _bSolicitudNuevaFoto = false;
                        _pantalla.ParametroAuxiliar = comandoJson.Operacion;
                    }));
                }
                else if (comandoJson.CodigoStatus == enmStatus.Ok
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
                else if (comandoJson.CodigoStatus == enmStatus.Ok
                        && comandoJson.Accion == enmAccion.PATENTE_OCR)
                {
                    Vehiculo vehiculo = Utiles.ClassUtiles.ExtraerObjetoJson<Vehiculo>(comandoJson.Operacion);
                    if (vehiculo != null && vehiculo.InfoOCRDelantero != null &&
                        !string.IsNullOrEmpty(vehiculo.InfoOCRDelantero.Patente))
                    {
                        Application.Current.Dispatcher.Invoke((Action)(() =>
                        {
                            txtPatenteOCR.Text = vehiculo.InfoOCRDelantero.Patente;
                            if (string.IsNullOrEmpty(txtPatente.Text))
                                txtPatente.Text = vehiculo.InfoOCRDelantero.Patente;
                        }));
                    }
                }
                else
                    bRet = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaPatente:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
            catch
            {

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
            if (_ventanaSimbolo != null && _ventanaSimbolo.TieneFoco)
            {
                if (Teclado.IsEscapeKey(tecla))
                {
                    _ventanaSimbolo.TieneFoco = false;
                    _ventanaSimbolo.Close();
                }
                else
                    _ventanaSimbolo.ProcesarTecla(tecla);
            }
            else
            {
                if (Teclado.IsConfirmationKey(tecla))
                {
                    if (txtPatente.Text.Length >= 0)
                    {
                        //Compruebo si el formato de la patente es correcto
                        _patenteIngresada = txtPatente.Text;
                        Vehiculo vehiculo = new Vehiculo();
                        vehiculo.Patente = _patenteIngresada;

                        if (Clases.Utiles.EsPatenteValida(Clases.Utiles.FormatoPatente.Cerrado, _patenteIngresada, _causa.Codigo == eCausas.TipoExento ? false : true))
                        {
                            if (_timerFoto.Enabled)
                                _timerFoto.Stop();

                            vehiculo.FormatoPatValido = true;

                            if( _causa.Codigo == eCausas.IngresoPatente || _causa.Codigo == eCausas.TipoExento)
                            {
                                Application.Current.Dispatcher.Invoke((Action)(() =>
                                {
                                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                                    _pantalla.TecladoOculto();
                                }));
                            }
                        }
                        else
                        {
                            //Si se ingreso la patente incorrecta, aviso al usuario y borro los datos
                            //ingresados
                            if( _causa.Codigo == eCausas.IngresoPatente || _causa.Codigo == eCausas.TipoExento )
                                _pantalla.MensajeDescripcion(msgFormatoPatenteErr);

                            _patenteIngresada = string.Empty;
                            //No se borra la patente para que la editen
                            vehiculo.FormatoPatValido = false;
                        }

                        Utiles.ClassUtiles.InsertarDatoVia( vehiculo, ref listaDV );
                        Utiles.ClassUtiles.InsertarDatoVia( _causa, ref listaDV );

                        EnviarDatosALogica( enmStatus.Ok, enmAccion.TRA_PATENTE, JsonConvert.SerializeObject( listaDV, jsonSerializerSettings ) );
                        // Comento para que no se cierre la subventana antes de que lógica confirme la placa
                        //_pantalla.TecladoOculto();
                        //_pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                    else
                        _pantalla.MensajeDescripcion(msgFormatoPatenteErr);
                }
                else if (Teclado.IsFunctionKey(tecla, "Foto"))
                {
                    if (!Teclado.IsExistingKey("ZoomFoto"))
                    {
                        _bZoomFoto = !_bZoomFoto;

                        if (_bZoomFoto)
                        {
                            //Inserto la foto con zoom en lugar de la anterior
                            panelFoto.Dispatcher.Invoke((Action)(() =>
                          {
                              if (panelFoto.Children.Count > 0)
                                  panelFoto.Children.RemoveAt(0);
                              panelFoto.Children.Insert(0, Clases.Utiles.CargarFotoRectangulo(_strPathFoto, Datos.AnchoFoto, Datos.AltoFoto, _bZoomFoto, _logger));
                          }));
                        }
                        else
                        {
                            //Solicito una nueva foto a logica
                            SolicitudNuevaFoto nuevaFoto = new SolicitudNuevaFoto(true);
                            EnviarDatosALogica(enmStatus.Tecla, enmAccion.T_FOTO, JsonConvert.SerializeObject(nuevaFoto, jsonSerializerSettings));
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
                else if (Teclado.IsFunctionKey(tecla, "Simbolo"))
                {
                    _ventanaSimbolo = new AgregarSimbolo(_pantalla);
                    _ventanaSimbolo.ChildUpdated -= ProcesarSimbolo;
                    _ventanaSimbolo.ChildUpdated += ProcesarSimbolo;
                    _posicionSubV = Clases.Utiles.FrameworkElementPointToScreenPoint(txtPatente);
                    _ventanaSimbolo.Top = _posicionSubV.Y - (_ventanaSimbolo.Height + txtPatente.ActualHeight);
                    _ventanaSimbolo.Left = _posicionSubV.X - (_ventanaSimbolo.Width / 2);
                    _ventanaSimbolo.Show();
                }
                else if (Teclado.IsFunctionKey(tecla, "ZoomFoto"))
                {
                    //Inserto la foto con zoom en lugar de la anterior
                    if (_bZoomFoto) _bZoomFoto = false;
                    else _bZoomFoto = true;
                    panelFoto.Dispatcher.Invoke((Action)(() =>
                    {
                        if (panelFoto.Children.Count > 0)
                            panelFoto.Children.RemoveAt(0);
                        panelFoto.Children.Insert(0, Clases.Utiles.CargarFotoRectangulo(_pantalla.ParametroAuxiliar, Datos.AnchoFoto, Datos.AltoFoto, _bZoomFoto, _logger));
                    }));
                }
                else if (Teclado.IsEscapeKey(tecla))
                {
                    if (_timerFoto.Enabled)
                        _timerFoto.Stop();
                    txtPatente.Text = string.Empty;
                    _pantalla.MensajeDescripcion(string.Empty);
                    _pantalla.MensajeDescripcion(string.Empty, true, 2);
                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    EnviarDatosALogica(enmStatus.Abortada, enmAccion.TRA_PATENTE, string.Empty);
                    _pantalla.TecladoOculto();
                }
                else if (Teclado.IsBackspaceKey(tecla))
                {
                    if (txtPatente.Text.Length > 0)
                        txtPatente.Text = txtPatente.Text.Remove(txtPatente.Text.Length - 1);
                }
                else
                {
                    if (txtPatente.Text.Length < _maximoCaracteresPatente)
                    {
                        if(tecla == Key.F3)
                            txtPatente.Text += "Ñ";
                        else if (Teclado.IsLowerCaseOrNumberKey(tecla))
                            txtPatente.Text += Teclado.GetKeyAlphaNumericValue(tecla);
                    }
                }
            }
        }
        #endregion

        private void ProcesarSimbolo(Opcion item)
        {
            _ventanaSimbolo.Close();
            if (txtPatente.Text.Length < _maximoCaracteresPatente)
            {
                string sNuevoSimb = item == null ? string.Empty : item?.Descripcion;
                txtPatente.Text = txtPatente.Text + sNuevoSimb;
            }
        }

        private void PATENTE_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.TecladoVisible();
        }

        private void ENTER_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(System.Windows.Input.Key.Enter);
        }

        private void ESC_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(System.Windows.Input.Key.Escape);
        }

        private void PATENTE_Click(object sender, MouseButtonEventArgs e)
        {

        }
    }
}
