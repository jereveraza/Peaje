using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Clases;
using System.Windows.Controls;
using System.Collections.Generic;
using Newtonsoft.Json;
using Entidades.Comunicacion;
using Entidades;
using Entidades.Logica;
using System.Windows.Input;
using Utiles.Utiles;
using System.Windows.Media;
using System.Linq;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmMenuVenta { SeleccionMonto, IngresoMonto, ConfirmaMonto, ConfirmaOpcion }

    /// <summary>
    /// Lógica de interacción para VentanaVenta.xaml
    /// </summary>
    public partial class VentanaRecarga : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private int _cantidadOpcionesMenu;
        private int _OpcionElegida;
        private enmMenuVenta _enmMenuVenta;
        private ListBoxItem _ItemSeleccionado;
        private bool _bPrimerDigito = true;
        private bool _cashFromTouch = false;
        private ListadoOpciones _listadoOpciones = new ListadoOpciones();
        private ListBox _listBoxMenu;
        private int _caracteresSimboloMoneda;
        private string _nroTag;
        private bool _permiteIngresarOtroMonto = false;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgSeleccionoMonto = "Seleccione la opción y confirme con {0}";
        const string msgIngreseMonto = "Ingrese el monto a recargar y confirme";
        const string msgConfirmeMonto = "Recarga Seleccionada: {0}";
        const string msgConfirmeOpcion = "Recarga Seleccionada: {0}";
        const string msgConfirmeConTecla = "Confirme con tecla {0}";
        #endregion

        #region Constructor de la clase
        public VentanaRecarga(IPantalla padre)
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
            _bPrimerDigito = false;
            _enmMenuVenta = enmMenuVenta.SeleccionMonto;
            if (_pantalla.ParametroAuxiliar != string.Empty)
            {
                //Cargo la lista al control
                DibujarMenuOpciones();
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
            FrameworkElement control = (FrameworkElement)borderVentanaVenta.Child;
            borderVentanaVenta.Child = null;
            Close();
            return control;
        }
        #endregion      

        #region Metodo que carga la lista de opciones al listbox
        private void DibujarMenuOpciones()
        {
            Vehiculo vehiculo = Utiles.ClassUtiles.ExtraerObjetoJson<Vehiculo>(_pantalla.ParametroAuxiliar);
            _listadoOpciones = Utiles.ClassUtiles.ExtraerObjetoJson<ListadoOpciones>(_pantalla.ParametroAuxiliar);

            int index = 1;
            IList<string> items1 = new List<string>();
            IList<string> items2 = new List<string>();
            _OpcionElegida = 0;
            _cantidadOpcionesMenu = 10;
            _pantalla.MensajeDescripcion(
                string.Format(Traduccion.Traducir(msgSeleccionoMonto),
                Teclado.GetEtiquetaTecla("Enter")),
                false);
            SetTextoBotonesAceptarCancelar("Enter", "Escape");

            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                txtBoxPatente.Text = vehiculo.Patente;
                txtBoxColor.Text = vehiculo.InfoTag.Color;
                txtBoxCategoria.Text = vehiculo.CategoDescripcionLarga;
                txtBoxMarca.Text = vehiculo.InfoTag.Marca;
                txtBoxModelo.Text = vehiculo.InfoTag.Modelo;
                txtBoxNombre.Text = vehiculo.InfoTag.NombreCuenta;
                //txtBoxNumeroTag.Text = vehiculo.InfoTag.NumeroTag.Replace(" ","");
                _nroTag = vehiculo.InfoTag.NumeroTag;
                lblMenuOpcion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                _caracteresSimboloMoneda = Datos.GetSimboloMonedaReferencia().Length;

                _cantidadOpcionesMenu = _listadoOpciones.ListaOpciones.Count;

                foreach (Opcion opcion in _listadoOpciones.ListaOpciones)
                {
                    if (opcion.Objeto != "*")
                    {
                        if (index < 6)
                            items1.Add(string.Format("{0:00} :   {1}{2}", opcion.Orden, Datos.GetSimboloMonedaReferencia(), opcion.Descripcion));
                        else
                            items2.Add(string.Format("{0:00} :   {1}{2}", opcion.Orden, Datos.GetSimboloMonedaReferencia(), opcion.Descripcion));
                    }
                    else
                    {
                        _permiteIngresarOtroMonto = true;
                        lblOtros.Text = string.Format("{0:00} :   {0}", opcion.Orden, opcion.Descripcion);
                        txtOtroMonto.Text = string.Empty;
                        stackOtros.Visibility = Visibility.Visible;
                    }
                    opcion.OrdenReal = index;
                    index++;
                }
                listBoxMenu1.ItemsSource = items1;
                listBoxMenu2.ItemsSource = items2;
                lblMenuOpcion.Text = string.Empty;
            }));          
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
                else if (comandoJson.Accion == enmAccion.ESTADO_SUB &&
                            (comandoJson.CodigoStatus == enmStatus.FallaCritica ||
                             comandoJson.CodigoStatus == enmStatus.Ok))
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }));
                }
                else if (comandoJson.CodigoStatus == enmStatus.Error)
                {
                    if (_enmMenuVenta == enmMenuVenta.ConfirmaMonto)
                    {
                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            _enmMenuVenta = enmMenuVenta.IngresoMonto;
                            lblMenuOpcion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        }));
                    }
                }
                else
                    bRet = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaVenta:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
            catch (Exception ex)
            {
                _logger.Debug("VentanaVenta:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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
            else if (Teclado.IsConfirmationKey(tecla) && (_enmMenuVenta != enmMenuVenta.ConfirmaOpcion && _enmMenuVenta != enmMenuVenta.ConfirmaMonto))
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
            else
            {
                if (_enmMenuVenta == enmMenuVenta.ConfirmaMonto || _enmMenuVenta == enmMenuVenta.ConfirmaOpcion)
                {
                    if (Teclado.IsCashKey(tecla))
                        btnAceptar.Dispatcher.Invoke((Action)(() =>
                        {
                            btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyleHighlighted);
                        }));
                }
                else
                {
                    if (Teclado.IsConfirmationKey(tecla))
                        btnAceptar.Dispatcher.Invoke((Action)(() =>
                        {
                            btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyleHighlighted);
                        }));
                }
            }

            List<DatoVia> listaDV = new List<DatoVia>();
            if (Teclado.IsEscapeKey(tecla))
            {
                if (_enmMenuVenta == enmMenuVenta.SeleccionMonto)
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        EnviarDatosALogica(enmStatus.Abortada, enmAccion.RECARGA,String.Empty );
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.MensajeDescripcion(string.Empty,false,2);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }));
                }
                else if (_enmMenuVenta == enmMenuVenta.IngresoMonto)
                {
                    _enmMenuVenta = enmMenuVenta.SeleccionMonto;
                    SetTextoBotonesAceptarCancelar("Enter", "Escape");
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(
                        string.Format(Traduccion.Traducir(msgSeleccionoMonto),
                        Teclado.GetEtiquetaTecla("Enter")),
                        false);
                        txtOtroMonto.Style = Estilo.FindResource<Style>(ResourceList.TextBoxListStyle);
                        txtOtroMonto.Visibility = Visibility.Collapsed;
                        lblMenuOpcion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                        DibujarMenuOpciones();
                    }));
                }
                else if (_enmMenuVenta == enmMenuVenta.ConfirmaMonto)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _enmMenuVenta = enmMenuVenta.IngresoMonto;
                        lblMenuOpcion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                        _pantalla.MensajeDescripcion(msgIngreseMonto);
                        _pantalla.MensajeDescripcion(string.Empty, false, 2);
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyle);
                    }));
                }
                else if (_enmMenuVenta == enmMenuVenta.ConfirmaOpcion)
                {
                    _enmMenuVenta = enmMenuVenta.SeleccionMonto;
                    SetTextoBotonesAceptarCancelar("Enter", "Escape");
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        lblMenuOpcion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                        btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyle);
                        _pantalla.MensajeDescripcion(
                        string.Format(Traduccion.Traducir(msgSeleccionoMonto),
                        Teclado.GetEtiquetaTecla("Enter")),
                        false);
                        _pantalla.MensajeDescripcion(string.Empty, false, 2);
                    }));
                }
            }
            else if (Teclado.IsUpKey(tecla))
            {
                
            }
            else if (Teclado.IsDownKey(tecla))
            {
                
            }
            else if (Teclado.IsNumericKey(tecla) || Teclado.IsDecimalKey(tecla))
            {
                int teclaNumerica = (int)Teclado.GetKeyNumericValue(tecla);
                if (_enmMenuVenta == enmMenuVenta.SeleccionMonto && teclaNumerica > -1)
                {
                    if (!_bPrimerDigito)
                    {
                        lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = (_OpcionElegida / 10).ToString() + "_"));
                        _OpcionElegida = teclaNumerica * 10;
                        _bPrimerDigito = true;
                    }
                    else
                    {
                        switch (teclaNumerica)
                        {
                            case 1:
                                _OpcionElegida = 1;
                                break;
                            case 2:
                                _OpcionElegida = 2;
                                break;
                            case 3:
                                _OpcionElegida = 2;
                                break;
                            case 4:
                                _OpcionElegida = 4;
                                break;
                            case 5:
                                _OpcionElegida = 5;
                                break;
                            case 6:
                                _OpcionElegida = 6;
                                break;
                            case 7:
                                _OpcionElegida = 7;
                                break;
                            case 8:
                                _OpcionElegida = 8;
                                break;
                            case 9:
                                _OpcionElegida = 9;
                                break;
                        }
                        //_OpcionElegida += teclaNumerica;
                        lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString("D2")));
                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            SeleccionarItemOrdenMostrado(_OpcionElegida);
                        }));
                        _bPrimerDigito = false;
                    }
                }
                else if (_enmMenuVenta == enmMenuVenta.IngresoMonto)
                {
                    _logger.Info("VentanaVenta:ProcesarTecla() -> IngresoMonto");
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        if ((Teclado.IsNumericKey(tecla) || Teclado.IsDecimalKey(tecla)) && txtOtroMonto.Text.Length < (_caracteresSimboloMoneda + 5))
                        {
                            if (Teclado.IsDecimalKey(tecla) && !txtOtroMonto.Text.Contains("."))
                                txtOtroMonto.Text += ".";
                            else
                            {
                                int digito = Teclado.GetKeyNumericValue(tecla);
                                int cantCifrasDecimales;
                                if (txtOtroMonto.Text.Contains("."))
                                    cantCifrasDecimales = txtOtroMonto.Text.Length - txtOtroMonto.Text.IndexOf(".");
                                else
                                    cantCifrasDecimales = 0;

                                if (digito > -1 && cantCifrasDecimales <= Datos.GetCantidadDecimales())
                                    txtOtroMonto.Text += digito;
                            }
                            _logger.Info("VentanaVenta:ProcesarTecla() -> Monto digitado [{0}]", txtOtroMonto.Text);
                        }
                    }));
                }
            }
            else if (Teclado.IsBackspaceKey(tecla))
            {
                if (_enmMenuVenta == enmMenuVenta.IngresoMonto)
                {
                    if (txtOtroMonto.Text.Length > _caracteresSimboloMoneda)
                        txtOtroMonto.Text = txtOtroMonto.Text.Remove(txtOtroMonto.Text.Length - 1);
                }
                else if (_enmMenuVenta == enmMenuVenta.SeleccionMonto)
                {
                    if (_bPrimerDigito)
                    {
                        lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = string.Empty));
                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            SeleccionarItem(-1);
                        }));
                        _bPrimerDigito = false;
                    }
                }
            }
            else if (Teclado.IsConfirmationKey(tecla) && !_cashFromTouch)
            {
                switch (lblMenuOpcion.Text)
                {
                    case "1_":
                        _OpcionElegida = 1;
                        break;
                    case "2_":
                        _OpcionElegida = 2;
                        break;
                    case "3_":
                        _OpcionElegida = 2;
                        break;
                    case "4_":
                        _OpcionElegida = 4;
                        break;
                    case "5_":
                        _OpcionElegida = 5;
                        break;
                    case "6_":
                        _OpcionElegida = 6;
                        break;
                    case "7_":
                        _OpcionElegida = 7;
                        break;
                    case "8_":
                        _OpcionElegida = 8;
                        break;
                    case "9_":
                        _OpcionElegida = 9;
                        break;
                }
                if (_enmMenuVenta == enmMenuVenta.SeleccionMonto)
                {
                    if (_permiteIngresarOtroMonto && _OpcionElegida == _cantidadOpcionesMenu)
                    {
                        _enmMenuVenta = enmMenuVenta.IngresoMonto;
                        _pantalla.MensajeDescripcion(msgIngreseMonto);
                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            txtOtroMonto.Style = Estilo.FindResource<Style>(ResourceList.TextBoxListHighlightedStyle);
                            txtOtroMonto.Visibility = Visibility.Visible;
                            txtOtroMonto.Text = Datos.GetSimboloMonedaReferencia();
                            lblMenuOpcion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                            SeleccionarItemOrdenMostrado(_OpcionElegida);
                        }));
                    }
                    else if ( (_OpcionElegida < _cantidadOpcionesMenu || !_permiteIngresarOtroMonto) && 
                              _OpcionElegida > 0)
                    {
                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            var opccion = _listadoOpciones.ListaOpciones.First(x => x.Orden == _OpcionElegida);
                            _enmMenuVenta = enmMenuVenta.ConfirmaOpcion;
                            _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgConfirmeOpcion),
                                Datos.FormatearMonedaAString(Convert.ToDecimal(opccion.Descripcion))),
                                false
                                );
                            _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgConfirmeConTecla),
                                Teclado.GetEtiquetaTecla("Cash")),
                                false, 2
                                );
                            SetTextoBotonesAceptarCancelar("Cash", "Escape");
                            Application.Current.Dispatcher.Invoke((Action)(() =>
                            {
                                btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                            }));
                        }));
                    }
                }
                else if (_permiteIngresarOtroMonto && _enmMenuVenta == enmMenuVenta.IngresoMonto)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        int CantDigitos = txtOtroMonto.Text.Contains(".") ? _caracteresSimboloMoneda + 1 :
                                            _caracteresSimboloMoneda;
                        if (txtOtroMonto.Text.Length > CantDigitos)
                        {
                            _enmMenuVenta = enmMenuVenta.ConfirmaMonto;
                            _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgConfirmeMonto),
                                txtOtroMonto.Text),
                                false
                                );
                            _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgConfirmeConTecla),
                                Teclado.GetEtiquetaTecla("Cash")),
                                false, 2
                                );
                            SetTextoBotonesAceptarCancelar("Cash", "Escape");
                            Application.Current.Dispatcher.Invoke((Action)(() =>
                            {
                                btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                            }));
                        }
                    }));
                } 
            }
            else if (Teclado.IsCashKey(tecla) || _cashFromTouch)
            {
                _cashFromTouch = false;

                if (_enmMenuVenta == enmMenuVenta.ConfirmaMonto)
                {
                    Venta recarga = new Venta(txtBoxPatente.Text, _nroTag, decimal.Parse(txtOtroMonto.Text.Remove(0, _caracteresSimboloMoneda).Replace('.',',')));
                    Utiles.ClassUtiles.InsertarDatoVia(recarga, ref listaDV);
                    EnviarDatosALogica(enmStatus.Ok, enmAccion.RECARGA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    _pantalla.MensajeDescripcion(string.Empty);
                }
                else if (_enmMenuVenta == enmMenuVenta.ConfirmaOpcion)
                {
                    var opcion = _listadoOpciones.ListaOpciones.First(x => x.Orden == _OpcionElegida);
                    string descripcionMonto = opcion.Descripcion;
                    long monto = (long)Convert.ToDouble(descripcionMonto);
                    Venta recarga = new Venta(txtBoxPatente.Text, _nroTag, monto);
                    Utiles.ClassUtiles.InsertarDatoVia(recarga, ref listaDV);
                    EnviarDatosALogica(enmStatus.Ok, enmAccion.RECARGA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    _pantalla.MensajeDescripcion(string.Empty);
                }
            }
        }
        #endregion

        #region Metodo que selecciona un item del listbox
        private void SeleccionarItemOrdenMostrado(int orden)
        {
            if (_listadoOpciones.ListaOpciones.Any(s => s.Orden == orden))
            {
                int posicion;

                if (orden == _cantidadOpcionesMenu)
                    posicion = _cantidadOpcionesMenu;
                else
                    posicion = _listadoOpciones.ListaOpciones.First(s => s.Orden == orden).OrdenReal;

                SeleccionarItem(posicion - 1);
            }
            else
            {
                _OpcionElegida = 0;
                SeleccionarItem(-1);
                lblMenuOpcion.Dispatcher.Invoke((Action)(() => lblMenuOpcion.Text = string.Empty));
            }
        }

        /// <summary>
        /// Metodo que resalta la opcion seleccionada
        /// </summary>
        /// <param name="posicion"></param>
        private void SeleccionarItem(int posicion)
        {
            ListBoxItem item = new ListBoxItem();
            ListBox listBox;
            int posicionAux;

            listBoxMenu2.SelectedIndex = -1;
            listBoxMenu1.SelectedIndex = -1;
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                stackOtros.Background = null;
                lblOtros.Style = Estilo.FindResource<Style>(ResourceList.ListBoxTextStyle);
            }));

            if (posicion == -1)
                return;

            if ((posicion + 1) == _cantidadOpcionesMenu && _permiteIngresarOtroMonto)
            {
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    stackOtros.Background = Brushes.White;
                    lblOtros.Style = Estilo.FindResource<Style>(ResourceList.ListBoxTextStyleInverted);
                }));
            }
            else
            {
                if (posicion < 5)
                {
                    listBox = listBoxMenu1;
                    posicionAux = posicion;
                }
                else
                {
                    listBox = listBoxMenu2;
                    posicionAux = posicion - 5;
                }

                for (int i = 0; i < listBox.Items.Count; i++)
                {
                    item = listBox.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                    if (item != null)
                    {
                        //item.Background = null;
                        if (i == posicionAux)
                        {
                            //item.Style = Estilo.FindResource<Style>(ResourceList.ListBoxItemStyle);
                            //item.Background = (Brush)new BrushConverter().ConvertFrom(background);
                            _ItemSeleccionado = item;
                            listBox.SelectedIndex = posicionAux;
                            listBox.SelectedItem = _ItemSeleccionado;
                            listBox.UpdateLayout();
                            listBox.ScrollIntoView(listBox.SelectedItem);
                            listBox.UpdateLayout();
                        }
                    }
                }
            }
        }
        #endregion

        private void OnPreviewMouseDown1(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(sender as ListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null)
            {
                string aux = (string)item.Content;
                string aux2 = $"{aux[0]}{aux[1]}";

                _OpcionElegida = Int32.Parse(aux2);

                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
                listBoxMenu1.Dispatcher.BeginInvoke((Action)(() =>
                {
                    SeleccionarItemOrdenMostrado(_OpcionElegida);
                }));


                _ItemSeleccionado = item;
                listBoxMenu1.SelectedItem = _ItemSeleccionado;
                listBoxMenu1.UpdateLayout();
                listBoxMenu1.ScrollIntoView(listBoxMenu1.SelectedItem);
                listBoxMenu1.UpdateLayout();
            }
        }

        private void OnPreviewMouseDown2(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(sender as ListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null)
            {
                string aux = (string)item.Content;
                string aux2 = $"{aux[0]}{aux[1]}";

                _OpcionElegida = Int32.Parse(aux2);

                lblMenuOpcion.Dispatcher.BeginInvoke((Action)(() => lblMenuOpcion.Text = _OpcionElegida.ToString()));
                listBoxMenu1.Dispatcher.BeginInvoke((Action)(() =>
                {
                    SeleccionarItemOrdenMostrado(_OpcionElegida);
                }));


                _ItemSeleccionado = item;
                listBoxMenu2.SelectedItem = _ItemSeleccionado;
                listBoxMenu2.UpdateLayout();
                listBoxMenu2.ScrollIntoView(listBoxMenu2.SelectedItem);
                listBoxMenu2.UpdateLayout();
            }
        }

        private void ENTER_Click(object sender, RoutedEventArgs e)
        {
            if (_enmMenuVenta == enmMenuVenta.ConfirmaOpcion || _enmMenuVenta == enmMenuVenta.ConfirmaMonto)
                _cashFromTouch = true;
            
            ProcesarTecla(Key.Enter);
        }

        private void ESC_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(Key.Escape);
        }
    }
}
