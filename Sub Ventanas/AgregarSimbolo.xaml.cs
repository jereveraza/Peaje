using Entidades;
using Entidades.Comunicacion;
using ModuloPantallaTeclado.Clases;
using ModuloPantallaTeclado.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    /// <summary>
    /// Lógica de interacción para AgregarSimbolo.xaml
    /// </summary>
    public partial class AgregarSimbolo : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmMenuPrincipal _enmMenuPrincipal;
        private ListBoxItem _ItemSeleccionado;
        private int _cantidadOpcionesMenu;
        private bool _bPrimerDigito = false;
        private int _OpcionElegida;
        private string _colorItemSeleccionado;
        private ListadoOpciones _listaOpciones = new ListadoOpciones();
        private Causa _causa = null;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private static AgregarSimbolo _helper;
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public event Action<Opcion> ChildUpdated;
        public bool TieneFoco { get; set; }
        #endregion

        #region Mensajes de descripcion
        const string msgSeleccionOpciones = "Seleccione un Símbolo";
        #endregion

        #region Constructor de la clase
        /// <summary>
        /// Constructor de la clase
        /// </summary>
        /// <param name="pantalla"></param>
        public AgregarSimbolo(IPantalla pantalla)
        {
            InitializeComponent();
            _pantalla = pantalla;
            _enmMenuPrincipal = enmMenuPrincipal.Seleccion;
            listBoxMenu.Items.Clear();
            _bPrimerDigito = false;
            NameValueCollection color = (NameValueCollection)ConfigurationManager.GetSection("color");
            _colorItemSeleccionado = color["itemSeleccionado"];
            _helper = this;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            TieneFoco = true;

            Clases.Utiles.TraducirControles<TextBlock>(borderSimbolos);

            try
            {
                string simb = Clases.Utiles.ObtenerSimbolos();
                _listaOpciones = Utiles.ClassUtiles.ExtraerObjetoJson<ListadoOpciones>(simb);
                _causa = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(simb);

                _cantidadOpcionesMenu = _listaOpciones.ListaOpciones.Count;
                int nroOpcion = 1;
                foreach (var si in _listaOpciones.ListaOpciones)
                {
                    if (_cantidadOpcionesMenu < 10)
                        listBoxMenu.Items.Add(nroOpcion.ToString() + " :\t" + si.Descripcion);
                    else
                        listBoxMenu.Items.Add(nroOpcion.ToString("00") + " :\t" + si.Descripcion);
                    nroOpcion++;
                }
                _OpcionElegida = 1;
                lblMenuOpcion.Text = _OpcionElegida.ToString();
                listBoxMenu.SelectedIndex = 0;
                SeleccionarItem(_OpcionElegida - 1, _colorItemSeleccionado);
                _pantalla.MensajeDescripcion(Traduccion.Traducir(msgSeleccionOpciones) + " [1 - " + _listaOpciones.ListaOpciones.Count + "]", false);
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("AgregarSimbolo:Grid_Loaded() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("AgregarSimbolo:Grid_Loaded() Exception: {0}", ex.Message.ToString());
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
            FrameworkElement control = (FrameworkElement)borderSimbolos.Child;
            borderSimbolos.Child = null;
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
                _logger.Debug("IngresoSistema:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar enviar datos a logica.");
            }
        }
        #endregion

        #region Metodo de procesamiento de tecla recibida
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
                EnviarDatosALogica(enmStatus.Abortada, enmAccion.OBSERVACION, string.Empty);
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
                        lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
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
                        lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
                    }
                }
                else if (Teclado.IsNumericKey(tecla))
                {
                    int teclaNumerica = (int)Teclado.GetKeyNumericValue(tecla);
                    //Si solamente tengo 9 opciones para mostrar en la lista
                    if (_cantidadOpcionesMenu < 10)
                    {
                        _OpcionElegida = teclaNumerica;
                        if (teclaNumerica >= 1 && teclaNumerica <= _cantidadOpcionesMenu)
                        {
                            lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
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
                                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = (_OpcionElegida / 10).ToString() + "_"));
                                _OpcionElegida = teclaNumerica * 10;
                                _bPrimerDigito = true;
                            }
                            else
                                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = string.Empty));
                        }
                        else
                        {
                            if (_OpcionElegida + teclaNumerica <= _cantidadOpcionesMenu)
                            {
                                _OpcionElegida += teclaNumerica;
                                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
                                listBoxMenu.Dispatcher.BeginInvoke((Action)(() =>
                                {
                                    SeleccionarItem(_OpcionElegida - 1, _colorItemSeleccionado);
                                }));
                            }
                            else
                                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = string.Empty));
                            _bPrimerDigito = false;
                        }
                    }
                }
                else if (Teclado.IsConfirmationKey(tecla))
                {
                    if (_OpcionElegida > 0 && _OpcionElegida <= _cantidadOpcionesMenu)
                    {
                        ChildUpdated(_listaOpciones.ListaOpciones[_OpcionElegida - 1]);
                        TieneFoco = false;
                        _pantalla.MensajeDescripcion(string.Empty);
                    }
                }
            }
        }

        public void ProcesarTeclaUp(Key tecla)
        {

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

        public static void CerrarVentanaSimbolo()
        {
            if (_helper != null && _helper.IsLoaded)
                _helper.Close();
        }
    }
}
