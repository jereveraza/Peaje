using System.Windows;
using System.Windows.Controls;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Entidades;
using ModuloPantallaTeclado.Clases;
using System.Collections.Specialized;
using System.Configuration;
using Newtonsoft.Json;
using System;
using System.Windows.Media;
using Entidades.Comunicacion;
using Entidades;
using System.Collections.Generic;
using System.Windows.Input;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    /// <summary>
    /// Lógica de interacción para VentanaRecorridos.xaml
    /// </summary>
    public partial class VentanaRecorridos : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmMenuPrincipal _enmMenuPrincipal;
        private ListBoxItem _ItemSeleccionado;
        private int _cantidadOpcionesMenu;
        private bool _bPrimerDigito = false;
        private int _OpcionElegida;
        private string _colorItemSeleccionado;
        private ListadoRecorridos _listaRecorridos = new ListadoRecorridos();
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgSeleccionOpciones = "Seleccione una opcion [0-9] y presione {0}";
        #endregion

        #region Constructor de la clase
        /// <summary>
        /// Constructor de la clase
        /// </summary>
        /// <param name="padre"></param>
        public VentanaRecorridos(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
            _enmMenuPrincipal = enmMenuPrincipal.Seleccion;
            listBoxMenu.Items.Clear();
            _bPrimerDigito = false;
            NameValueCollection color = (NameValueCollection)ConfigurationManager.GetSection("color");
            _colorItemSeleccionado = color["itemSeleccionado"];
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            Clases.Utiles.TraducirControles<TextBlock>(Grid_Principal);
            try
            {
                //Cargo las opciones del listbox
                if (_pantalla.ParametroAuxiliar != string.Empty)
                {                    
                    _listaRecorridos = Utiles.ClassUtiles.ExtraerObjetoJson<ListadoRecorridos>(_pantalla.ParametroAuxiliar);
                    int nroOpcion = 1;
                    foreach (var rec in _listaRecorridos.ListaRecorridos)
                    {
                        listBoxMenu.Items.Add(nroOpcion.ToString("00") + " :\t" + rec.Descripcion);
                        nroOpcion++;
                    }
                    _cantidadOpcionesMenu = _listaRecorridos.ListaRecorridos.Count;
                    _OpcionElegida = 1;
                    lblMenuOpcion.Content = _OpcionElegida.ToString();
                    listBoxMenu.SelectedIndex = 0;
                    SeleccionarItem(_OpcionElegida - 1, _colorItemSeleccionado);
                    _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgSeleccionOpciones),
                                Teclado.GetEtiquetaTecla("Enter")),
                                false
                                );
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaRecorridos:Grid_Loaded() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaRecorridos:Grid_Loaded() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
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
            FrameworkElement control = (FrameworkElement)borderVentanaRecorrido.Child;
            borderVentanaRecorrido.Child = null;
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
                _logger.Debug("VentanaRecorridos:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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
                _pantalla.MensajeDescripcion(string.Empty);
                _pantalla.CargarSubVentana(enmSubVentana.Principal);
                EnviarDatosALogica(enmStatus.Abortada, enmAccion.RECORRIDO, string.Empty);
            }

            if (_enmMenuPrincipal == enmMenuPrincipal.Seleccion)
            {
                if (Teclado.IsUpKey(tecla))
                {
                    if (_OpcionElegida > 1)
                    {
                        listBoxMenu.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            _OpcionElegida--;
                            SeleccionarItem(_OpcionElegida - 1, _colorItemSeleccionado);
                        }));
                        lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Content = _OpcionElegida.ToString()));
                    }
                }
                else if (Teclado.IsDownKey(tecla))
                {
                    if (_OpcionElegida < _cantidadOpcionesMenu)
                    {
                        listBoxMenu.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            _OpcionElegida++;
                            SeleccionarItem(_OpcionElegida - 1, _colorItemSeleccionado);
                        }));
                        lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Content = _OpcionElegida.ToString()));
                    }
                }
                else if (Teclado.IsNumericKey(tecla))
                {
                    int teclaNumerica = Teclado.GetKeyNumericValue(tecla);
                    //Si solamente tengo 9 opciones para mostrar en la lista
                    if (_cantidadOpcionesMenu < 10)
                    {
                        _OpcionElegida = teclaNumerica;
                        if (teclaNumerica >= 1 && teclaNumerica <= _cantidadOpcionesMenu)
                        {
                            lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Content = _OpcionElegida.ToString()));
                            listBoxMenu.Dispatcher.BeginInvoke((Action)(() =>
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
                                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Content = (_OpcionElegida / 10).ToString() + "_"));
                                _OpcionElegida = teclaNumerica * 10;
                                _bPrimerDigito = true;
                            }
                            else
                                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Content = string.Empty));
                        }
                        else
                        {
                            if (_OpcionElegida + teclaNumerica <= _cantidadOpcionesMenu)
                            {
                                _OpcionElegida += teclaNumerica;
                                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Content = _OpcionElegida.ToString()));
                                listBoxMenu.Dispatcher.BeginInvoke((Action)(() =>
                                {
                                    SeleccionarItem(_OpcionElegida - 1, _colorItemSeleccionado);
                                }));
                            }
                            else
                                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Content = string.Empty));
                            _bPrimerDigito = false;
                        }
                    }
                }
                else if (Teclado.IsConfirmationKey(tecla))
                {
                    Recorrido recorrido = new Recorrido();
                    recorrido.Codigo = _listaRecorridos.ListaRecorridos[_OpcionElegida - 1].Codigo;
                    Utiles.ClassUtiles.InsertarDatoVia(recorrido, ref listaDV);
                    EnviarDatosALogica(enmStatus.Ok, enmAccion.RECORRIDO, Newtonsoft.Json.JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    _pantalla.MensajeDescripcion(string.Empty);
                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
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

            for (int i = 0; i < listBoxMenu.Items.Count; i++)
            {
                item = listBoxMenu.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (item != null)
                {
                    item.Background = null;
                    if (i == posicion)
                    {
                        item.Background = (Brush)new BrushConverter().ConvertFrom(background);
                        _ItemSeleccionado = item;
                        listBoxMenu.SelectedIndex = posicion;
                        listBoxMenu.SelectedItem = _ItemSeleccionado;
                        listBoxMenu.UpdateLayout();
                        listBoxMenu.ScrollIntoView(listBoxMenu.SelectedItem);
                        listBoxMenu.UpdateLayout();
                    }
                }
            }
        }
        #endregion

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(sender as ListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null)
            {
                string aux = (string)item.Content;
                string aux2 = $"{aux[0]}{aux[1]}";

                _OpcionElegida = Int32.Parse(aux2);

                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Content = _OpcionElegida.ToString()));
                listBoxMenu.Dispatcher.BeginInvoke((Action)(() =>
                {
                    SeleccionarItem(_OpcionElegida, _colorItemSeleccionado);
                }));


                _ItemSeleccionado = item;
                listBoxMenu.SelectedItem = _ItemSeleccionado;
                listBoxMenu.UpdateLayout();
                listBoxMenu.ScrollIntoView(listBoxMenu.SelectedItem);
                listBoxMenu.UpdateLayout();
            }
        }
    }
}
