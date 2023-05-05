using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Clases;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Specialized;
using System.Configuration;
using Newtonsoft.Json;
using Entidades.Comunicacion;
using Entidades;
using System.Collections.Generic;
using Entidades.ComunicacionBaseDatos;
using System.Windows.Input;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmMenuExento { IngresoPatente, ListaExentos }

    /// <summary>
    /// Lógica de interacción para VentanaExento.xaml
    /// </summary>
    public partial class VentanaExento : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmMenuExento _enmMenuExento = enmMenuExento.ListaExentos;
        private string _patenteIngresada;
        private int _cantidadOpcionesMenu;
        private bool _bPrimerDigito = false;
        private bool _bZoomFoto = false;
        private int _OpcionElegida;
        private string _colorItemSeleccionado;        
        private PatenteExenta _patenteExenta = null;
        private ListadoOpciones _listadoOpciones = null;
        private ListBoxItem _ItemSeleccionado;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgSeleccionOpciones = "Ingrese la patente";
        const string msgFormatoPatenteErr = "Formato de patente incorrecto";
        #endregion

        #region Constructor de la clase
        /// <summary>
        /// Constructor de la clase
        /// </summary>
        /// <param name="padre"></param>
        public VentanaExento(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
            NameValueCollection color = (NameValueCollection)ConfigurationManager.GetSection("color");
            _colorItemSeleccionado = color["itemSeleccionado"];
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            SetTextoBotonesAceptarCancelar("Enter", "Escape");
            Clases.Utiles.TraducirControles<TextBlock>(gridIngresoSistema);
            _enmMenuExento = enmMenuExento.ListaExentos;
            _patenteExenta = Utiles.ClassUtiles.ExtraerObjetoJson<PatenteExenta>(_pantalla.ParametroAuxiliar);
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                CargarDatosEnControles(_pantalla.ParametroAuxiliar);
            }));
            _pantalla.TecladoVisible();
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
            FrameworkElement control = (FrameworkElement)borderVentanaExento.Child;
            borderVentanaExento.Child = null;
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
                if (comandoJson.CodigoStatus == enmStatus.Ok
                    && comandoJson.Accion == enmAccion.LIST_EXENTO
                    && comandoJson.Operacion != string.Empty)
                {
                    try
                    {
                        ListadoOpciones _listadoOpciones = Utiles.ClassUtiles.ExtraerObjetoJson<ListadoOpciones>(comandoJson.Operacion);
                        _patenteExenta = Utiles.ClassUtiles.ExtraerObjetoJson<PatenteExenta>(comandoJson.Operacion);

                        int nroOpcion = 1;

                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            foreach (Opcion opcion in _listadoOpciones.ListaOpciones)
                            {
                                listaOpciones.Items.Add(nroOpcion.ToString() + " :\t" + opcion.Descripcion);
                                nroOpcion++;
                            }

                            _cantidadOpcionesMenu = _listadoOpciones.ListaOpciones.Count;
                            _OpcionElegida = 1;
                            lblMenuOpcion.Text = _OpcionElegida.ToString();
                            listaOpciones.SelectedIndex = 0;
                            SeleccionarItem(_OpcionElegida - 1, _colorItemSeleccionado);
                        }));
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.Debug("VentanaExento:RecibirDatosLogica() JsonException: {0}", jsonEx.Message.ToString());
                        _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug("VentanaExento:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
                        _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
                    }
                }
                else if (comandoJson.CodigoStatus == enmStatus.Abortada && comandoJson.Accion == enmAccion.EXENTO)
                {
                    //Logica indica que se debe cerrar la ventana
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }));
                }
                else
                    bRet = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaExento:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al recibir una Respuesta de logica.");
            }
            return bRet;
        }

        #region Metodo de carga de textboxes de datos
        /// <summary>
        /// 
        /// </summary>
        /// <param name="datos"></param>
        private void CargarDatosEnControles(string datos)
        {
            try
            {
                _listadoOpciones = Utiles.ClassUtiles.ExtraerObjetoJson<ListadoOpciones>(datos);

                int nroOpcion = 1;

                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    foreach (Opcion opcion in _listadoOpciones.ListaOpciones)
                    {
                        listaOpciones?.Items.Add(nroOpcion.ToString() + " :\t" + opcion.Descripcion);
                        nroOpcion++;
                    }

                    _cantidadOpcionesMenu = _listadoOpciones.ListaOpciones.Count;
                    _OpcionElegida = 1;
                    lblMenuOpcion.Text = _OpcionElegida.ToString();
                    listaOpciones.SelectedIndex = 0;
                    SeleccionarItem(_OpcionElegida - 1, _colorItemSeleccionado);
                }));
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaExento:CargarDatosEnControles() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaExento:CargarDatosEnControles() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

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
                _logger.Debug("VentanaExento:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                btnAceptar.Dispatcher.Invoke((Action)(() =>
                {
                    btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyleHighlighted);
                }));
            }

            List<DatoVia> listaDV = new List<DatoVia>();
            
            if (_enmMenuExento == enmMenuExento.ListaExentos)
            {
                if (Teclado.IsEscapeKey(tecla))
                {
                    Exento vehiculoExento = new Exento();
                    Utiles.ClassUtiles.InsertarDatoVia(vehiculoExento, ref listaDV);
                    EnviarDatosALogica(enmStatus.Abortada, enmAccion.EXENTO, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }));
                    _pantalla.TecladoOculto();
                }
                else if (Teclado.IsUpKey(tecla))
                {
                    if (_OpcionElegida > 1)
                    {
                        listaOpciones.Dispatcher.Invoke((Action)(() =>
                        {
                            _OpcionElegida--;
                            SeleccionarItem(_OpcionElegida - 1, _colorItemSeleccionado);
                        }));
                        lblMenuOpcion.Dispatcher.Invoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
                    }
                }
                else if (Teclado.IsDownKey(tecla))
                {
                    if (_OpcionElegida < _cantidadOpcionesMenu)
                    {
                        listaOpciones.Dispatcher.Invoke((Action)(() =>
                        {
                            _OpcionElegida++;
                            SeleccionarItem(_OpcionElegida - 1, _colorItemSeleccionado);
                        }));
                        lblMenuOpcion.Dispatcher.Invoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
                    }
                }
                else if (Teclado.IsNumericKey(tecla))
                {
                    int teclaNumerica = Teclado.GetKeyNumericValue(tecla);
                    //Si solamente tengo 9 opciones para mostrar en la lista
                    if (_cantidadOpcionesMenu < 10)
                    {
                        if (teclaNumerica >= 1 && teclaNumerica <= _cantidadOpcionesMenu)
                        {
                            _OpcionElegida = teclaNumerica;
                            lblMenuOpcion.Dispatcher.Invoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
                            listaOpciones.Dispatcher.Invoke((Action)(() =>
                            {
                                SeleccionarItem(_OpcionElegida - 1, _colorItemSeleccionado);
                            }));
                        }
                    }
                    else
                    {
                        if (!_bPrimerDigito)
                        {
                            if (teclaNumerica >= 0 && teclaNumerica <= _cantidadOpcionesMenu / 10)
                            {
                                lblMenuOpcion.Dispatcher.Invoke((Action)(() => lblMenuOpcion.Text = (_OpcionElegida / 10).ToString() + "_"));
                                _OpcionElegida = teclaNumerica * 10;
                                _bPrimerDigito = true;
                            }
                            else
                                lblMenuOpcion.Dispatcher.Invoke((Action)(() => lblMenuOpcion.Text = string.Empty));
                        }
                        else
                        {
                            if (_OpcionElegida + teclaNumerica <= _cantidadOpcionesMenu)
                            {
                                _OpcionElegida += teclaNumerica;
                                lblMenuOpcion.Dispatcher.Invoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
                                listaOpciones.Dispatcher.Invoke((Action)(() =>
                                {
                                    SeleccionarItem(_OpcionElegida - 1, _colorItemSeleccionado);
                                }));
                            }
                            else
                                lblMenuOpcion.Dispatcher.Invoke((Action)(() => lblMenuOpcion.Text = string.Empty));
                            _bPrimerDigito = false;
                        }
                    }
                }
                else if (Teclado.IsConfirmationKey(tecla))
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        Opcion opcion = _listadoOpciones.ListaOpciones[_OpcionElegida - 1];

                        Utiles.ClassUtiles.InsertarDatoVia(opcion, ref listaDV);
                        Utiles.ClassUtiles.InsertarDatoVia(_patenteExenta, ref listaDV);

                        EnviarDatosALogica(enmStatus.Ok, enmAccion.EXENTO, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        
                    }));
                    _pantalla.TecladoOculto();
                }
            }
        }
        #endregion

        #region Metodo que selecciona un item del listbox
        /// <summary>
        /// Metodo que resalta la opcion seleccionada
        /// </summary>
        /// <param name="posicion"></param>
        /// <param name="background"></param>
        private void SeleccionarItem(int posicion, string background)
        {
            ListBoxItem item = new ListBoxItem();

            for (int i = 0; i < listaOpciones.Items.Count; i++)
            {
                item = listaOpciones.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (item != null)
                {
                    item.Background = null;
                    if (i == posicion)
                    {
                        item.Background = (Brush)new BrushConverter().ConvertFrom(background);
                        _ItemSeleccionado = item;
                        listaOpciones.SelectedIndex = posicion;
                        listaOpciones.SelectedItem = _ItemSeleccionado;
                        listaOpciones.UpdateLayout();
                        listaOpciones.ScrollIntoView(listaOpciones.SelectedItem);
                        listaOpciones.UpdateLayout();
                    }
                }
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

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(sender as ListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null)
            {
                string aux = (string)item.Content;
                string aux2 = $"{aux[0]}{aux[1]}";

                _OpcionElegida = Int32.Parse(aux2);

                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
                listaOpciones.Dispatcher.BeginInvoke((Action)(() =>
                {
                    SeleccionarItem(_OpcionElegida, _colorItemSeleccionado);
                }));


                _ItemSeleccionado = item;
                listaOpciones.SelectedItem = _ItemSeleccionado;
                listaOpciones.UpdateLayout();
                listaOpciones.ScrollIntoView(listaOpciones.SelectedItem);
                listaOpciones.UpdateLayout();
            }
        }
    }
}
