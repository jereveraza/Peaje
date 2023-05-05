using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Clases;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Windows.Controls;
using Entidades.Comunicacion;
using Entidades;
using Entidades.Logica;
using System.Windows.Input;
using Utiles.Utiles;
using System.Linq;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmCobroDeuda { SeleccionDeuda, ObtenerDatos, ConfirmoPago }

    /// <summary>
    /// Lógica de interacción para VentanaCobroDeudas.xaml
    /// </summary>
    public partial class VentanaCobroDeudas : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmCobroDeuda _enmCobroDeuda;
        private List<InfoDeuda> _listaDeudas;
        private List<DeudaConIndex> _listaDeudasIndex;
        private List<InfoDeuda> _infoDeudasSeleccionadas;
        private InfoDeuda _pagoTotal;
        private int _cantidadDeudas = 0;
        private int _itemSeleccionado = 0;
        private int _paginaVisible = 0;
        private decimal _totalDeuda = 0;
        private string _patente;
        private Vehiculo _vehiculo;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        private class DeudaConIndex
        {
            public DeudaConIndex(InfoDeuda deuda, int num, int pag)
            {
                NumeroDeuda = num;
                InfoDeuda = deuda;
                Pagina = pag;
                DeudaSeleccionada = false;
            }

            public bool DeudaSeleccionada { get; set; }

            public int NumeroDeuda { get; set; }

            public int Pagina { get; set; }

            public string IdPago { get { if (InfoDeuda.Id == 0) return "-"; else return InfoDeuda.Id.ToString(); } }

            public string Tipo
            {
                get
                {
                    if (InfoDeuda.Tipo == eTipoDeuda.PagoDiferido)
                        return Traduccion.Traducir("P DIF");
                    else if (InfoDeuda.Tipo == eTipoDeuda.Violacion)
                        return Traduccion.Traducir("VIOL");
                    else
                        return Traduccion.Traducir("TODAS");
                }
            }
            public string Estacion { get { if (InfoDeuda.Estacion == 0) return "-"; else return InfoDeuda.NombreEstacion; } }

            public string Monto { get { return Datos.FormatearMonedaAString(InfoDeuda.Monto); } }

            public string FechaHora { get { if (InfoDeuda.Estacion == 0) return "-"; else return InfoDeuda.FechaHora.ToString("dd-MM-yy   HH:mm"); } }

            public InfoDeuda InfoDeuda { get; set; }
        }
        #endregion

        #region Mensajes de descripcion
        const string msgSeleccionDeudaMultiplesPaginas = "Seleccione las deudas de la lista, {0} Siguiente pag.";
        const string msgSeleccioneDeuda = "Agregue la deuda con {0}, {1} para volver.";
        const string msgDeseaConfirmarPago = "Confirme el pago de {0} con {1}, {2} para volver.";
        #endregion

        #region Defines
        private const int _filasVisiblesPrimerPaginaDeudas = 10;
        #endregion

        #region Constructor de la clase
        /// <summary>
        /// Constructor de la clase
        /// </summary>
        /// <param name="padre"></param>
        public VentanaCobroDeudas(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            _enmCobroDeuda = enmCobroDeuda.SeleccionDeuda;
            SetTextoBotonesAceptarCancelar("Cash", "Escape");
            Clases.Utiles.TraducirControles<TextBlock>(Grid_Principal);
            _infoDeudasSeleccionadas = new List<InfoDeuda>();
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                txtOpcion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgSeleccioneDeuda),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                _pantalla.TecladoVisible();
            }));

            try
            {
                _vehiculo = Utiles.ClassUtiles.ExtraerObjetoJson<Vehiculo>(_pantalla.ParametroAuxiliar);
                if (_vehiculo != null && !string.IsNullOrEmpty(_vehiculo.Patente))
                {
                    _patente = _vehiculo.Patente;
                    txtBoxPatente.Dispatcher.Invoke((Action)(() => txtBoxPatente.Text = _vehiculo.Patente));
                }

                _listaDeudas = Utiles.ClassUtiles.ExtraerObjetoJson<List<InfoDeuda>>(_pantalla.ParametroAuxiliar);
                _cantidadDeudas = _listaDeudas.Count;
                if (_cantidadDeudas != 0)
                {
                    _listaDeudasIndex = MapearListaDeudas(_listaDeudas);
                    CargarListaDeudasEnControl();
                    ResaltarDeudaSeleccionada(0);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("CobroDeudas:Grid_Loaded() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

        public void SetTextoBotonesAceptarCancelar(string TeclaConfirmacion, string TeclaCancelacion, bool BtnAceptarSiguiente = false)
        {
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                btnAceptar.Content = Traduccion.Traducir("Confirmar") + " [" + Teclado.GetEtiquetaTecla(TeclaConfirmacion) + "]";
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
            FrameworkElement control = (FrameworkElement)borderCobroDeudas.Child;
            borderCobroDeudas.Child = null;
            Close();
            return control;
        }
        #endregion

        #region Metodos de carga de lista de deudas

        /// <summary>
        /// Verifica si opcion ingresada se encuentra dentro del rango de la pagina de clientes visible en pantalla
        /// </summary>
        /// <returns></returns>
        private bool EstaEnRangoDePaginaVisible(int num)
        {
            bool ret = false;
            ret = _listaDeudasIndex.Exists(c => c.Pagina == _paginaVisible/* && c.NumeroDeuda == num*/);
            return ret;
        }

        /// <summary>
        /// Resalta la deuda seleccionada en la lista del control
        /// </summary>
        /// <param name="num"></param>
        /// <returns></returns>
        private void ResaltarDeudaSeleccionada(int num)
        {
            int index = 0;

            if (num == -1)
                index = -1;
            else
            {
                if (num <= _filasVisiblesPrimerPaginaDeudas)
                {
                    index = num - 1;
                }
                else
                {
                    index = ((num - 1) % _filasVisiblesPrimerPaginaDeudas) + 1;
                }
            }

            dataGridDeudas.Dispatcher.Invoke((Action)(() =>
            {
                dataGridDeudas.SelectedIndex = index;
                if (dataGridDeudas.SelectedItem != null)
                {
                    DeudaConIndex infoDeuda = dataGridDeudas.SelectedItem as DeudaConIndex;
                    if (_pagoTotal != null && infoDeuda.InfoDeuda == _pagoTotal)
                    {
                        dataGridDeudas.SelectedItem = infoDeuda.DeudaSeleccionada = !infoDeuda.DeudaSeleccionada;
                        _listaDeudasIndex = _listaDeudasIndex.Select(c => { c.DeudaSeleccionada = infoDeuda.DeudaSeleccionada; return c; }).ToList();
                    }
                    else
                    {
                        dataGridDeudas.SelectedItem = infoDeuda.DeudaSeleccionada = !infoDeuda.DeudaSeleccionada;
                    }
                    dataGridDeudas.ScrollIntoView(dataGridDeudas.SelectedItem);
                    CargarListaDeudasEnControl(_paginaVisible);
                }
            }));
        }

        private List<DeudaConIndex> MapearListaDeudas(List<InfoDeuda> listaInfoDeuda)
        {
            List<DeudaConIndex> lista = new List<DeudaConIndex>();
            int numeroCliente = 0, numeroDeuda = 1;
            int numeroPagina = 0;

            if (_cantidadDeudas > 1)
            {
                foreach (var d in listaInfoDeuda)
                {
                    _totalDeuda += d.Monto;
                }
                //Agrego la opcion de pagar todo al final de la lista
                _pagoTotal = new InfoDeuda(eTipoDeuda.Todas, 0, 0, _totalDeuda, DateTime.Now);
            }

            foreach (InfoDeuda id in listaInfoDeuda)
            {
                numeroPagina = (numeroCliente / _filasVisiblesPrimerPaginaDeudas) + 1;
                //if ((numeroCliente % _filasVisiblesPrimerPaginaDeudas == 0) && _cantidadDeudas > 1)
                //    lista.Add(new DeudaConIndex(_pagoTotal, 0, numeroPagina));
                //else
                numeroDeuda++;
                lista.Add(new DeudaConIndex(id, numeroDeuda-1, numeroPagina));
                
                numeroCliente++;
            }
            lista.Add(new DeudaConIndex(_pagoTotal, numeroDeuda, numeroPagina));

            return lista;
        }

        private void CargarListaDeudasEnControl(int numeroPagina = 1)
        {
            try
            {
                List<DeudaConIndex> lista = _listaDeudasIndex.FindAll(c => c.Pagina == numeroPagina);

                if (lista.Count > 0)
                {
                    _paginaVisible = numeroPagina;
                    if (_cantidadDeudas > 10)
                    {
                        txtTeclaSiguiente.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            txtTeclaSiguiente.Text = string.Format(Traduccion.Traducir(
                                            msgSeleccionDeudaMultiplesPaginas), Teclado.GetEtiquetaTecla("NextPage"));
                        }));
                    }

                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        dataGridDeudas.ItemsSource = lista;
                        dataGridDeudas.Items.Refresh();
                    }));
                }
                else
                {
                    CargarListaDeudasEnControl(1);   //Se vuelve a llamar a la primer pag. si se llega al final.
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaCobroDeudas:CargarListaDeudasEnControl() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaCobroDeudas:CargarListaDeudasEnControl() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
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
                else
                    bRet = true;
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("CobroDeudas:RecibirDatosLogica() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("CobroDeudas:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
                _logger.Debug("CobroDeudas:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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
            else if (Teclado.IsConfirmationKey(tecla) || Teclado.IsCashKey(tecla))
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
            else if (Teclado.IsConfirmationKey(tecla) || ( Teclado.IsCashKey(tecla) || tecla == Key.C))
            {
                btnAceptar.Dispatcher.Invoke((Action)(() =>
                {
                    btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyleHighlighted);
                }));
            }

            List<DatoVia> listaDV = new List<DatoVia>();
            if (Teclado.IsEscapeKey(tecla))
            {
                if (_enmCobroDeuda == enmCobroDeuda.SeleccionDeuda)
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }));
                }
                else if (_enmCobroDeuda == enmCobroDeuda.ConfirmoPago)
                {
                    _enmCobroDeuda = enmCobroDeuda.SeleccionDeuda;
                    txtOpcion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                    SetTextoBotonesAceptarCancelar("Enter", "Escape");
                    _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgSeleccioneDeuda),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                }
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                if (_enmCobroDeuda == enmCobroDeuda.SeleccionDeuda)
                {
                    int opcion = 0;
                    int.TryParse(txtOpcion.Text, out opcion);
                    opcion--;
                    if (EstaEnRangoDePaginaVisible(opcion))
                    {
                        if (opcion == 0)
                            _itemSeleccionado = 1;
                        else
                            _itemSeleccionado = opcion + 1;
                        ResaltarDeudaSeleccionada(_itemSeleccionado);
                        if (dataGridDeudas.SelectedItem != null)
                        {
                            InfoDeuda infoDeuda = (dataGridDeudas.SelectedItem as DeudaConIndex).InfoDeuda;
                            if (infoDeuda == _pagoTotal)
                            {
                                //Se eligio la opcion todas, agrego todas a la lista
                                _infoDeudasSeleccionadas.Clear();
                                if ((dataGridDeudas.SelectedItem as DeudaConIndex).DeudaSeleccionada)
                                    _infoDeudasSeleccionadas = _listaDeudas;
                            }
                            else
                            {
                                if (_infoDeudasSeleccionadas.Contains(infoDeuda))
                                    _infoDeudasSeleccionadas.Remove(infoDeuda);
                                else
                                    _infoDeudasSeleccionadas.Add(infoDeuda);
                            }
                        }
                    }
                    txtOpcion.Dispatcher.BeginInvoke((Action)(() => txtOpcion.Text = string.Empty));
                }
            }
            else if (Teclado.IsCashKey(tecla) || tecla == Key.Divide )
            {
                if (_enmCobroDeuda == enmCobroDeuda.SeleccionDeuda)
                {
                    if (_infoDeudasSeleccionadas.Count > 0)
                    {
                        _enmCobroDeuda = enmCobroDeuda.ConfirmoPago;
                        txtOpcion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                        txtOpcion.Dispatcher.BeginInvoke((Action)(() => txtOpcion.Text = string.Empty));
                        SetTextoBotonesAceptarCancelar("Cash", "Escape");
                        decimal totalDeuda = 0;
                        foreach (var d in _infoDeudasSeleccionadas)
                            totalDeuda += d.Monto;
                        _pantalla.MensajeDescripcion(
                                    string.Format(Traduccion.Traducir(msgDeseaConfirmarPago),
                                    Datos.FormatearMonedaAString(totalDeuda),
                                    Teclado.GetEtiquetaTecla("Cash"), Teclado.GetEtiquetaTecla("Escape")),
                                    false
                                    );
                    }
                }
                else if (_enmCobroDeuda == enmCobroDeuda.ConfirmoPago)
                {
                    if (_infoDeudasSeleccionadas.Count > 0)
                    {
                        _vehiculo.ListaInfoDeuda = _infoDeudasSeleccionadas;
                        _vehiculo.TipBo = ' ';

                        Utiles.ClassUtiles.InsertarDatoVia(_vehiculo, ref listaDV);

                        EnviarDatosALogica(enmStatus.Ok, enmAccion.COBRODEUDAS, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        Application.Current.Dispatcher.Invoke((Action)(() =>
                        {
                            _pantalla.MensajeDescripcion(string.Empty);
                            _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        }));
                    }
                }
            }          
            else if (Teclado.IsNextPageKey(tecla))
            {
                if (_enmCobroDeuda == enmCobroDeuda.SeleccionDeuda)
                    CargarListaDeudasEnControl(_paginaVisible + 1);
            }
            else if (Teclado.IsNumericKey(tecla))
            {
                int teclaNumerica = Teclado.GetKeyNumericValue(tecla);
                if (_enmCobroDeuda == enmCobroDeuda.SeleccionDeuda)
                {
                    int opcion = 0;
                    int.TryParse(txtOpcion.Text, out opcion);

                    if (txtOpcion.Text.Length == 1) //Tengo un solo digito cargado en textbox
                    {
                        txtOpcion.Dispatcher.Invoke((Action)(() =>
                        {
                            txtOpcion.Text = string.Format("{0}{1}", opcion, teclaNumerica);
                        }));
                    }
                    else //Tengo dos digitos cargados en textbox
                    {
                        // Borro dos digitos de texbox e ingreso nuevo digito
                        txtOpcion.Dispatcher.Invoke((Action)(() =>
                        {
                            txtOpcion.Text = string.Format("{0}", teclaNumerica);
                        }));
                    }
                }
            }
        }
        #endregion

        #region Pantalla Tactil

        private void ENTER_Click(object sender, RoutedEventArgs e)
        {
            if(_enmCobroDeuda == enmCobroDeuda.ObtenerDatos)
                ProcesarTecla(Key.Enter);
            else
                ProcesarTecla(Key.Divide);
        }

        private void ESC_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(Key.Escape);
        }

        #endregion
    }
}

