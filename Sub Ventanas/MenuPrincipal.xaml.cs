using System;
using System.Collections.Generic;
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
using System.Windows.Input;
using Utiles;
using Utiles.Utiles;
using System.Linq;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmMenuPrincipal { IngresoOpcion, Seleccion, ConfirmarOpcion }

    /// <summary>
    /// Lógica de interacción para MenuPrincipal.xaml
    /// </summary>
    public partial class MenuPrincipal : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmMenuPrincipal _enmMenuPrincipal;
        private ListBoxItem _ItemSeleccionado;
        private int _cantidadOpcionesMenu;
        private bool _bPrimerDigito = false;
        private int _OpcionElegida;
        private string _colorItemSeleccionado;
        private ListadoOpciones _listaOpcionesMenu = new ListadoOpciones();
        private Causa _causa;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private object objParameter = null;
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgSeleccionOpciones = "Seleccione una opcion";
        const string msgConfirmeOpcionElegida = "Confirme la opcion elegida";
        #endregion

        #region Constructor de la clase
        /// <summary>
        /// Constructor de la clase
        /// </summary>
        /// <param name="padre"></param>
        

        public MenuPrincipal(IPantalla padre, object obj = null)
        {
            InitializeComponent();
            _pantalla = padre;
            _enmMenuPrincipal = enmMenuPrincipal.IngresoOpcion;
            listBoxMenu.Items.Clear();
            _bPrimerDigito = false;
            NameValueCollection color = (NameValueCollection)ConfigurationManager.GetSection("color");
            _colorItemSeleccionado = color["itemSeleccionado"];
            _listaOpcionesMenu.ListaOpciones.Clear();

            if( obj != null )
                objParameter = obj;

            _pantalla.TecladoVisible();
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            SetTextoBotonesAceptarCancelar("Enter", "Escape");
            Clases.Utiles.TraducirControles<TextBlock>(gridIngresoSistema);
            try
            {
                //Cargo las opciones del listbox
                if (_pantalla.ParametroAuxiliar != string.Empty)
                {
                    ListadoOpciones listaOpciones = ClassUtiles.ExtraerObjetoJson<ListadoOpciones>(_pantalla.ParametroAuxiliar);
                    _causa = ClassUtiles.ExtraerObjetoJson<Causa>(_pantalla.ParametroAuxiliar);
                    _logger.Info("MenuPrincipal -> {0}", _causa.Descripcion);

                    // Si se recibe parametro de que NO se muestra un listado de una sola opcion, se autoselecciona y envia dicha opcion
                    if (!listaOpciones.MuestraOpcionIndividual
                    && listaOpciones.ListaOpciones.Count == 1)
                    {
                        List<DatoVia> listaDV = new List<DatoVia>();
                        var opcion = listaOpciones.ListaOpciones[0];
                        Utiles.ClassUtiles.InsertarDatoVia(opcion, ref listaDV);
                        Utiles.ClassUtiles.InsertarDatoVia(_causa, ref listaDV);
                        var opcionSerializada = JsonConvert.SerializeObject(listaDV, jsonSerializerSettings);
                        EnviarDatosALogica(enmStatus.Ok, enmAccion.OPCION_MENU, opcionSerializada);
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                    else
                    {
                        lblTituloMenu.Dispatcher.Invoke((Action)(() => lblTituloMenu.Text = Traduccion.Traducir(_causa.Descripcion)));
                        int nroOpcion = 0;
                        listaOpciones.ListaOpciones.Sort((x, y) => x.Orden.CompareTo(y.Orden));
                        _cantidadOpcionesMenu = listaOpciones.ListaOpciones.Count;
                        foreach (var opt in listaOpciones.ListaOpciones)
                        {
                            //if (_cantidadOpcionesMenu < 10)
                            if( !listaOpciones.ListaOpciones.Any(x => x.Orden > 9))
                                listBoxMenu.Items.Add(opt.Orden.ToString() + " :\t" + opt.Descripcion);
                            else
                                listBoxMenu.Items.Add(opt.Orden.ToString("00") + " :\t" + opt.Descripcion);
                            _listaOpcionesMenu.ListaOpciones.Add(opt);

                            opt.OrdenReal = nroOpcion;
                            nroOpcion++;
                        }
                        _OpcionElegida = listaOpciones.ListaOpciones[0].Orden;
                        lblMenuOpcion.Text = _OpcionElegida.ToString();
                        listBoxMenu.SelectedIndex = 0;
                        SeleccionarItemOrdenReal(_OpcionElegida - 1, _colorItemSeleccionado);
                        _pantalla.MensajeDescripcion(Traduccion.Traducir(msgSeleccionOpciones),false);
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("MenuPrincipal:Grid_Loaded() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("MenuPrincipal:Grid_Loaded() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
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
            FrameworkElement control = (FrameworkElement)borderMenuPrincipal.Child;
            borderMenuPrincipal.Child = null;
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
                    && comandoJson.Accion == enmAccion.ESTADO)
                {
                    bool cierroVentana = false;
                    Causa causa = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(comandoJson.Operacion);

                    if(_causa.Codigo == eCausas.AperturaTurno)
                    {
                        if (causa.Codigo == eCausas.AperturaTurno
                            || causa.Codigo == eCausas.CausaCierre)
                        {
                            cierroVentana = true;
                        }
                    }
                    else if(_causa.Codigo == eCausas.CausaCierre)
                    {
                        if (causa.Codigo == eCausas.AperturaTurno
                            || causa.Codigo == eCausas.CausaCierre)
                        {
                            cierroVentana = true;
                        }
                    }
                    else if (_causa.Codigo == eCausas.TipoExento)
                    {
                        if (causa.Codigo == eCausas.AperturaTurno
                            || causa.Codigo == eCausas.CausaCierre
                            || causa.Codigo == eCausas.Salidavehiculo)
                        {
                            cierroVentana = true;
                        }
                    }
                    else if (_causa.Codigo == eCausas.CausaSimulacion)
                    {
                        cierroVentana = true;
                    }
                    else if (_causa.Codigo == eCausas.CausaCancelacion)
                    {
                        cierroVentana = true;
                    }
                    else if (_causa.Codigo == eCausas.Observacion)
                    {
                        if (causa.Codigo == eCausas.AperturaTurno
                            || causa.Codigo == eCausas.CausaCierre)
                        {
                            cierroVentana = true;
                        }
                    }
                    else if (_causa.Codigo == eCausas.Retiro)
                    {
                        if (causa.Codigo == eCausas.AperturaTurno
                            || causa.Codigo == eCausas.CausaCierre)
                        {
                            cierroVentana = true;
                        }
                    }
                    else if (_causa.Codigo == eCausas.VentaRecarga)
                    {
                        if (causa.Codigo == eCausas.AperturaTurno
                            || causa.Codigo == eCausas.CausaCierre
                            || causa.Codigo == eCausas.Salidavehiculo)
                        {
                            cierroVentana = true;
                        }
                    }

                    if (cierroVentana)
                    {
                        //Logica indica que se debe cerrar la ventana
                        Application.Current.Dispatcher.Invoke((Action)(() =>
                        {
                            _pantalla.MensajeDescripcion(string.Empty);
                            _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        }));
                    }
                }
                else if (comandoJson.CodigoStatus == enmStatus.Error)
                {
                    _enmMenuPrincipal = enmMenuPrincipal.IngresoOpcion;
                }
                else if (comandoJson.Accion == enmAccion.ESTADO_SUB &&
                        (comandoJson.CodigoStatus == enmStatus.FallaCritica || comandoJson.CodigoStatus == enmStatus.Ok))
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }));
                }
                else
                    bRet = true;
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("MenuPrincipal:RecibirDatosLogica() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("MenuPrincipal:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
                _logger.Debug("MenuPrincipal:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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
            if (Teclado.IsEscapeKey(tecla))
            {
                EnviarDatosALogica(enmStatus.Abortada, enmAccion.OPCION_MENU, string.Empty);
                _pantalla.MensajeDescripcion(string.Empty);
                _pantalla.CargarSubVentana(enmSubVentana.Principal);
                _pantalla.TecladoOculto();
            }
            else
            {
                if (_enmMenuPrincipal == enmMenuPrincipal.IngresoOpcion)
                {
                    if (Teclado.IsUpKey(tecla))
                    {
                        if (_OpcionElegida > 1)
                        {
                            listBoxMenu.Dispatcher.BeginInvoke((Action)(() =>
                            {
                                _OpcionElegida--;
                                SeleccionarItemOrdenReal(_OpcionElegida - 1, _colorItemSeleccionado);
                            }));

                            int opcion = _listaOpcionesMenu.ListaOpciones.First( x => x.OrdenReal == _OpcionElegida - 2 ).Orden;

                            lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = opcion.ToString()));
                        }
                    }
                    else if (Teclado.IsDownKey(tecla))
                    {
                        if (_OpcionElegida < _cantidadOpcionesMenu)
                        {
                            listBoxMenu.Dispatcher.BeginInvoke((Action)(() =>
                            {
                                _OpcionElegida++;
                                SeleccionarItemOrdenReal(_OpcionElegida - 1, _colorItemSeleccionado);
                            }));

                            int opcion = _listaOpcionesMenu.ListaOpciones.First( x => x.OrdenReal == _OpcionElegida ).Orden;
                            lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = opcion.ToString()));
                        }
                    }
                    else if (Teclado.IsNumericKey(tecla))
                    {
                        int teclaNumerica = Teclado.GetKeyNumericValue(tecla);
                        //Si solamente tengo 9 opciones para mostrar en la lista
                        //if (_cantidadOpcionesMenu < 10)
                        if( !_listaOpcionesMenu.ListaOpciones.Any( x => x.Orden > 9 ) )
                        {
                            
                            //if (teclaNumerica >= 1 && teclaNumerica <= _cantidadOpcionesMenu)
                            if( _listaOpcionesMenu.ListaOpciones.Any( x => x.Orden == teclaNumerica ) )
                            {
                                _OpcionElegida = teclaNumerica;

                                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
                                listBoxMenu.Dispatcher.BeginInvoke((Action)(() =>
                                {
                                    SeleccionarItemOrdenMostrado( _OpcionElegida, _colorItemSeleccionado );
                                }));

                                // Opciones de un solo digito piden confirmacion inmediata sin ENTER
                                var opcion = _listaOpcionesMenu.ListaOpciones.First( x => x.Orden == _OpcionElegida );

                                if( opcion.Confirmar )
                                {
                                    _enmMenuPrincipal = enmMenuPrincipal.ConfirmarOpcion;
                                    _pantalla.MensajeDescripcion( msgConfirmeOpcionElegida );
                                }
                                else
                                {
                                    Utiles.ClassUtiles.InsertarDatoVia( opcion, ref listaDV );
                                    Utiles.ClassUtiles.InsertarDatoVia( _causa, ref listaDV );
                                    var opcionSerializada = JsonConvert.SerializeObject( listaDV, jsonSerializerSettings );
                                    EnviarDatosALogica( enmStatus.Ok, enmAccion.OPCION_MENU, opcionSerializada );
                                    _pantalla.MensajeDescripcion( string.Empty );
                                    _pantalla.CargarSubVentana( enmSubVentana.Principal );
                                } 
                            }
                        }
                        else
                        {
                            if (!_bPrimerDigito)
                            {
                                if (teclaNumerica >= 0)
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
                                //if (_OpcionElegida + teclaNumerica <= _cantidadOpcionesMenu)
                                //{
                                    _OpcionElegida += teclaNumerica;
                                    lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
                                    listBoxMenu.Dispatcher.BeginInvoke((Action)(() =>
                                    {
                                        SeleccionarItemOrdenMostrado( _OpcionElegida, _colorItemSeleccionado );
                                        //SeleccionarItemOrdenReal(_OpcionElegida - 1, _colorItemSeleccionado);
                                    }));
                                //}
                                //else
                                //    lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = string.Empty));
                                _bPrimerDigito = false;
                            }
                        }
                    }
                    else if (Teclado.IsConfirmationKey(tecla))
                    {                       
                        switch(lblMenuOpcion.Text)
                        {
                            case "1_":
                                lblMenuOpcion.Text = "01";
                                break;
                            case "2_":
                                lblMenuOpcion.Text = "02";
                                break;
                            case "3_":
                                lblMenuOpcion.Text = "03";
                                break;
                            case "4_":
                                lblMenuOpcion.Text = "04";
                                break;
                            case "5_":
                                lblMenuOpcion.Text = "05";
                                break;
                            case "6_":
                                lblMenuOpcion.Text = "06";
                                break;
                            case "7_":
                                lblMenuOpcion.Text = "07";
                                break;
                            case "8_":
                                lblMenuOpcion.Text = "08";
                                break;
                            case "9_":
                                lblMenuOpcion.Text = "09";
                                break;
                        }
                        var opcion = _listaOpcionesMenu.ListaOpciones.First(x => x.Orden == int.Parse(lblMenuOpcion.Text));
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        Utiles.ClassUtiles.InsertarDatoVia(opcion, ref listaDV);
                        Utiles.ClassUtiles.InsertarDatoVia(_causa, ref listaDV);
                        Utiles.ClassUtiles.InsertarDatoVia( objParameter, ref listaDV );

                        _pantalla.TecladoOculto();
                        var opcionSerializada = JsonConvert.SerializeObject(listaDV, jsonSerializerSettings);
                        EnviarDatosALogica(enmStatus.Ok, enmAccion.OPCION_MENU, opcionSerializada);
                        _pantalla.MensajeDescripcion(string.Empty);
                        if (_causa.Codigo != eCausas.OpcionesSupervisor)
                            _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        
                    }
                }
                else if(_enmMenuPrincipal == enmMenuPrincipal.ConfirmarOpcion)
                {
                    if (Teclado.IsConfirmationKey(tecla))
                    {
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        var opcion = _listaOpcionesMenu.ListaOpciones.First(x => x.Orden == int.Parse(lblMenuOpcion.Text));
                        Utiles.ClassUtiles.InsertarDatoVia(opcion, ref listaDV);
                        Utiles.ClassUtiles.InsertarDatoVia(_causa, ref listaDV);
                        Utiles.ClassUtiles.InsertarDatoVia( objParameter, ref listaDV );
                        var opcionSerializada = JsonConvert.SerializeObject(listaDV, jsonSerializerSettings);
                        EnviarDatosALogica(enmStatus.Ok, enmAccion.OPCION_MENU, opcionSerializada);
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.TecladoOculto();

                        if (_causa.Codigo != eCausas.OpcionesSupervisor)
                            _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                    else if (Teclado.IsNumericKey(tecla))
                    {
                        // Si son opciones de un solo digito se muestra mensaje de confirmacion sin ENTER, en caso de
                        //  reingreso de digito se selecciona la nueva opcion de nuevo
                        //if (_cantidadOpcionesMenu < 10)
                        if( !_listaOpcionesMenu.ListaOpciones.Any( x => x.Orden > 9 ) )
                        {
                            int teclaNumerica = Teclado.GetKeyNumericValue(tecla);

                            //if (teclaNumerica >= 1 && teclaNumerica <= _cantidadOpcionesMenu)
                            if( _listaOpcionesMenu.ListaOpciones.Any( x => x.Orden == teclaNumerica ) )
                            {
                                _OpcionElegida = teclaNumerica;
                                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
                                listBoxMenu.Dispatcher.BeginInvoke((Action)(() =>
                                {
                                    SeleccionarItemOrdenMostrado( _OpcionElegida, _colorItemSeleccionado );
                                    //SeleccionarItemOrdenReal(_OpcionElegida - 1, _colorItemSeleccionado);
                                }));

                                // Opciones de un solo digito piden confirmacion inmediata sin ENTER
                                var opcion = _listaOpcionesMenu.ListaOpciones.First( x => x.Orden == _OpcionElegida );
                                if (opcion.Confirmar)
                                {
                                    _pantalla.MensajeDescripcion(msgConfirmeOpcionElegida);
                                }
                                else
                                {
                                    Utiles.ClassUtiles.InsertarDatoVia(opcion, ref listaDV);
                                    Utiles.ClassUtiles.InsertarDatoVia(_causa, ref listaDV);
                                    var opcionSerializada = JsonConvert.SerializeObject(listaDV, jsonSerializerSettings);
                                    EnviarDatosALogica(enmStatus.Ok, enmAccion.OPCION_MENU, opcionSerializada);
                                    _pantalla.MensajeDescripcion(string.Empty);
                                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                                    _pantalla.TecladoOculto();
                                }
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region Procesamiento para pantalla tactil

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(sender as ListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null)
            {
                string aux = (string)item.Content;
                string aux2 = $"{aux[0]}{aux[1]}";

                _OpcionElegida = Int32.Parse(aux2);

                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
                listBoxMenu.Dispatcher.BeginInvoke((Action)(() =>
                {
                    SeleccionarItemOrdenMostrado(_OpcionElegida, _colorItemSeleccionado);
                }));


                _ItemSeleccionado = item;
                listBoxMenu.SelectedItem = _ItemSeleccionado;
                listBoxMenu.UpdateLayout();
                listBoxMenu.ScrollIntoView(listBoxMenu.SelectedItem);
                listBoxMenu.UpdateLayout();
            }
        }

        private void ENTER_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(System.Windows.Input.Key.Enter);
        }

        private void ESC_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(System.Windows.Input.Key.Escape);
        }

        #endregion

        #region Metodo que selecciona un item del listbox
        /// <summary>
        /// Metodo que resalta la opcion seleccionada
        /// </summary>
        /// <param name="posicion"></param>
        /// <param name="background"></param>
        private void SeleccionarItemOrdenReal(int posicion, string background)
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

        private void SeleccionarItemOrdenMostrado( int orden, string background )
        {
            if( _listaOpcionesMenu.ListaOpciones.Any( s => s.Orden == orden ) )
            {
                int posicion = _listaOpcionesMenu.ListaOpciones.First( s => s.Orden == orden ).OrdenReal;

                SeleccionarItemOrdenReal( posicion, background ); 
            }
        }

        


        #endregion
    }
}
