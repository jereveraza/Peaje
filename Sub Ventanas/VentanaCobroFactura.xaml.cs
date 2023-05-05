using ModuloPantallaTeclado.Interfaces;
using System;
using System.Collections.Generic;
using System.Windows;
using Entidades;
using Entidades.Comunicacion;
using Newtonsoft.Json;
using ModuloPantallaTeclado.Clases;
using Entidades.Logica;
using System.Windows.Controls;
using System.Windows.Input;
using Utiles.Utiles;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmMenuFactura { IngresoClave, IngresoRut, IngresoRazonSocial, IngresoNuevaRazonSocial, ConsultaDatos, SeleccionCliente, MuestroDatosClave, MuestroDatosRuc, MuestroDatosRazonSocial, ConfirmarDatos}

    /// <summary>
    /// Lógica de interacción para VentanaCobroFactura.xaml
    /// </summary>
    public partial class VentanaCobroFactura : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmMenuFactura _enmMenuFactura, _enmLastMenuFactura;
        private string _claveIngresada = string.Empty;
        private string _rucIngresado = string.Empty;
        private string _razonSocialIngresada = string.Empty;
        private InfoCliente _infoCliente = null;
        private List<InfoCliente> _listaClientes;
        private List<ClienteConIndex> _listaClientesIndex;
        private int _cantidadClientes = 0;
        private int _itemSeleccionado = 0;
        private int _paginaVisible = 0;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private AgregarSimbolo _ventanaSimbolo;
        private Point _posicionSubV;
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        private class ClienteConIndex
        {
            public ClienteConIndex(InfoCliente client, int num, int pag)
            {
                Header = 1;
                NumeroCliente = num;
                InfoCliente = client;
                Pagina = pag;
            }

            public int Header { get; set;}
            public int NumeroCliente { get; set; }

            public int Pagina { get; set; }

            public InfoCliente InfoCliente { get; set; }
        }
        #endregion

        #region Mensajes de descripcion
        const string msgIngresoClave = "Ingrese la clave de cliente";
        const string msgIngresoRut = "Ingrese el RUC";
        const string msgIngresoRutInvalido = "El Ruc ingresado no es valido";
        const string msgNuevoCliente = "Ruc no hallado. Ingrese razon social";
        const string msgIngresoRazonSocial = "Ingrese la Razon Social";
        const string msgRazonSocialCorta = "{0} caracteres como mínimo";
        const string msgClienteNoHallado = "Cliente no encontrado";
        const string msgSeleccionCliente = "Seleccione cliente de la lista";
        const string msgSeleccionClienteMultiplesPaginas = "Seleccione cliente de la lista, {0} Siguiente pag.";
        const string msgConfirmaDatos = "Confirme los datos seleccionados";
        const string msgSiguientePagClientes = "Pagina Siguiente...";
        const string msgConsultandoDatos = "Consultando datos...";
        #endregion

        #region Defines
        private const int _minimaCantidadCaracteresRazonSocialParaAutocompletar = 5;
        private const int _filasVisiblesPrimerPaginaClientes = 10;
        private const int _maxNumeroCaracteresClave = 12;
        private const int _maxNumeroCaracteresRuc = 13;
        private const int _maxNumeroCaracteresRazonSocial = 150;
        #endregion

        #region Constructor de la clase
        public VentanaCobroFactura(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        /// <summary>
        /// Funcion callback a cargarse la ventana
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            SetTextoBotonesAceptarCancelar("Enter", "Escape");
            _pantalla.TecladoVisible();
            btnTarjeta.Dispatcher.Invoke(() =>
            {
                btnTarjeta.Visibility = Visibility.Collapsed;
            });
            btnNext.Dispatcher.Invoke(() =>
            {
                btnNext.Visibility = Visibility.Collapsed;
            });
        }
        #endregion

        #region Metodo para enviar datos a logica
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
                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    _pantalla.EnviarDatosALogica(status, Accion, Operacion);
                }));
            }
            catch(Exception e)
            {
                _logger.Error(e);
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
            FrameworkElement control = (FrameworkElement)borderVentanaCobroFactura.Child;
            borderVentanaCobroFactura.Child = null;
            Close();
            return control;
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
            else if ((Teclado.IsConfirmationKey(tecla) && (_enmMenuFactura != enmMenuFactura.MuestroDatosClave
                    && _enmMenuFactura != enmMenuFactura.MuestroDatosRazonSocial
                    && _enmMenuFactura != enmMenuFactura.MuestroDatosRuc))
                || Teclado.IsCashKey(tecla))
            {
                btnAceptar.Dispatcher.Invoke((Action)(() =>
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
                if (_enmMenuFactura == enmMenuFactura.MuestroDatosClave
                    || _enmMenuFactura == enmMenuFactura.MuestroDatosRazonSocial
                    || _enmMenuFactura == enmMenuFactura.MuestroDatosRuc)
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
                if (_enmMenuFactura == enmMenuFactura.IngresoClave)
                {
                    if (Teclado.IsConfirmationKey(tecla))
                    {
                        _claveIngresada = txtClave.Text;

                        // Si no hay input, aviso por pantalla
                        if (_claveIngresada.Length == 0)
                        {
                            _pantalla.MensajeDescripcion(msgIngresoRut);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                            _enmMenuFactura = enmMenuFactura.IngresoRut;

                            FormatearTextBox(eTextBoxVentanaFactura.Clave, false);
                            FormatearTextBox(eTextBoxVentanaFactura.Ruc);
                        }
                        else
                        {
                            EstadoFactura estadoFactura = new EstadoFactura();
                            estadoFactura.Codigo = eBusquedaFactura.BusquedaNumeroCliente;
                            Utiles.ClassUtiles.InsertarDatoVia(estadoFactura, ref listaDV);

                            InfoCliente infoCliente = new InfoCliente();
                            int nroClave;
                            int.TryParse(_claveIngresada, out nroClave);
                            infoCliente.Clave = nroClave;
                            Utiles.ClassUtiles.InsertarDatoVia(infoCliente, ref listaDV);

                            _enmMenuFactura = enmMenuFactura.ConsultaDatos;
                            _pantalla.MensajeDescripcion(msgConsultandoDatos);

                            EnviarDatosALogica(enmStatus.Ok, enmAccion.FACTURA, Newtonsoft.Json.JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        }
                    }
                    else if (Teclado.IsEscapeKey(tecla))
                    {
                        _claveIngresada = txtClave.Text;

                        if (_claveIngresada.Length == 0)
                        {
                            EnviarDatosALogica(enmStatus.Abortada, enmAccion.FACTURA, string.Empty);
                            Application.Current.Dispatcher.Invoke((Action)(() =>
                            {
                                _pantalla.MensajeDescripcion(string.Empty);
                                _pantalla.CargarSubVentana(enmSubVentana.Principal);
                                _pantalla.TecladoOculto();
                                _pantalla.CargarSubVentana(enmSubVentana.FormaPago);
                            }));
                        }
                        else
                        {
                            CargarClaveEnControl();
                            _pantalla.MensajeDescripcion(msgIngresoClave);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        }
                    }
                    else if (Teclado.IsBackspaceKey(tecla))
                    {
                        if (txtClave.Text.Length > 0)
                        {
                            if (txtClave.SelectionStart == 0)
                                txtClave.SelectionStart += 1;
                            int selectionStart = txtClave.SelectionStart;
                            string currentText = txtClave.Text;

                            // Eliminar el carácter en la posición del cursor
                            string newText = currentText.Remove(selectionStart - 1, 1);

                            // Actualizar el texto y la posición del cursor
                            txtClave.Text = newText;
                            txtClave.SelectionStart = selectionStart - 1;
                        }
                    }
                    else
                    {
                        if (Teclado.IsNumericKey(tecla)
                             && txtClave.Text.Length < _maxNumeroCaracteresClave)
                        {
                            int selectionStart = txtClave.SelectionStart;
                            string currentText = txtClave.Text;

                            string newText = currentText.Insert(selectionStart, Teclado.GetKeyAlphaNumericValue(tecla).ToString());

                            txtClave.Text = newText;
                            txtClave.SelectionStart = selectionStart + 1;
                        }
                    }
                }
                else if (_enmMenuFactura == enmMenuFactura.IngresoRut)
                {
                    if (Teclado.IsConfirmationKey(tecla))
                    {
                        _rucIngresado = txtRuc.Text;

                        if (_rucIngresado.Length == 0)
                        {
                            _enmMenuFactura = enmMenuFactura.IngresoRazonSocial;
                            _pantalla.MensajeDescripcion(msgIngresoRazonSocial);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                            FormatearTextBox(eTextBoxVentanaFactura.Ruc, false);
                            FormatearTextBox(eTextBoxVentanaFactura.RazonSocial);
                        }
                        else
                        {
                            if (Clases.Utiles.ValidaRucPeru(_rucIngresado))
                            {
                                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                                {
                                    EstadoFactura estadoFactura = new EstadoFactura();
                                    estadoFactura.Codigo = eBusquedaFactura.BusquedaRut;
                                    Utiles.ClassUtiles.InsertarDatoVia(estadoFactura, ref listaDV);

                                    InfoCliente infoCliente = new InfoCliente();
                                    infoCliente.Ruc = _rucIngresado;
                                    Utiles.ClassUtiles.InsertarDatoVia(infoCliente, ref listaDV);

                                    _enmMenuFactura = enmMenuFactura.ConsultaDatos;
                                    _pantalla.MensajeDescripcion(msgConsultandoDatos);

                                    EnviarDatosALogica(enmStatus.Ok, enmAccion.FACTURA, Newtonsoft.Json.JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                                }));
                            }
                            else
                            {
                                _pantalla.MensajeDescripcion(msgIngresoRutInvalido);
                            }
                        }
                    }
                    else if (Teclado.IsEscapeKey(tecla))
                    {
                        _rucIngresado = txtRuc.Text;

                        if (_rucIngresado.Length == 0)
                        {
                            _pantalla.MensajeDescripcion(msgIngresoClave);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                            _enmMenuFactura = enmMenuFactura.IngresoClave;
                            FormatearTextBox(eTextBoxVentanaFactura.Ruc, false);
                            FormatearTextBox(eTextBoxVentanaFactura.Clave);
                        }
                        else
                        {
                            CargarRucEnControl();
                            _pantalla.MensajeDescripcion(msgIngresoRut);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        }

                    }
                    else if (Teclado.IsBackspaceKey(tecla))
                    {
                        if (txtRuc.Text.Length > 0)
                        {
                            if (txtRuc.SelectionStart == 0)
                                txtRuc.SelectionStart += 1;
                            int selectionStart = txtRuc.SelectionStart;
                            string currentText = txtRuc.Text;

                            // Eliminar el carácter en la posición del cursor
                            string newText = currentText.Remove(selectionStart - 1, 1);

                            // Actualizar el texto y la posición del cursor
                            txtRuc.Text = newText;
                            txtRuc.SelectionStart = selectionStart - 1;
                        }
                    }
                    else
                    {
                        if (Teclado.IsNumericKey(tecla))
                        {
                            if (txtRuc.Text.Length < _maxNumeroCaracteresRuc)
                            {
                                int selectionStart = txtRuc.SelectionStart;
                                string currentText = txtRuc.Text;

                                string newText = currentText.Insert(selectionStart, Teclado.GetKeyAlphaNumericValue(tecla).ToString());

                                txtRuc.Text = newText;
                                txtRuc.SelectionStart = selectionStart + 1;
                            }
                        }
                    }
                }
                else if (_enmMenuFactura == enmMenuFactura.IngresoRazonSocial)
                {
                    if (Teclado.IsConfirmationKey(tecla))
                    {
                        _razonSocialIngresada = txtRazonSocial.Text;

                        if (_razonSocialIngresada.Length >= _minimaCantidadCaracteresRazonSocialParaAutocompletar)
                        {
                            EstadoFactura estadoFactura = new EstadoFactura();
                            estadoFactura.Codigo = eBusquedaFactura.BusquedaNombre;
                            Utiles.ClassUtiles.InsertarDatoVia(estadoFactura, ref listaDV);

                            InfoCliente infoCliente = new InfoCliente();
                            infoCliente.RazonSocial = _razonSocialIngresada;
                            infoCliente.Nombre = _razonSocialIngresada;
                            Utiles.ClassUtiles.InsertarDatoVia(infoCliente, ref listaDV);

                            _enmMenuFactura = enmMenuFactura.ConsultaDatos;
                            EnviarDatosALogica(enmStatus.Ok, enmAccion.FACTURA, Newtonsoft.Json.JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));

                            Application.Current.Dispatcher.Invoke((Action)(() =>
                            {
                                _pantalla.MensajeDescripcion(msgConsultandoDatos);
                            }));
                        }
                        else
                        {
                            _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgRazonSocialCorta),
                                _minimaCantidadCaracteresRazonSocialParaAutocompletar.ToString()),
                                false
                                );
                        }
                    }
                    else if (Teclado.IsEscapeKey(tecla))
                    {
                        _razonSocialIngresada = txtRazonSocial.Text;

                        if (_razonSocialIngresada.Length == 0)
                        {
                            _enmMenuFactura = enmMenuFactura.IngresoRut;
                            _pantalla.MensajeDescripcion(msgIngresoRut);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                            FormatearTextBox(eTextBoxVentanaFactura.RazonSocial, false);
                            FormatearTextBox(eTextBoxVentanaFactura.Ruc);
                        }
                        else
                        {
                            CargarRazonSocialEnControl();
                            _pantalla.MensajeDescripcion(msgIngresoRazonSocial);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        }
                    }
                    else if (Teclado.IsBackspaceKey(tecla))
                    {
                        if (txtRazonSocial.Text.Length > 0)
                        {
                            if (txtRazonSocial.SelectionStart == 0)
                                txtRazonSocial.SelectionStart += 1;
                            int selectionStart = txtRazonSocial.SelectionStart;
                            string currentText = txtRazonSocial.Text;

                            // Eliminar el carácter en la posición del cursor
                            string newText = currentText.Remove(selectionStart - 1, 1);

                            // Actualizar el texto y la posición del cursor
                            txtRazonSocial.Text = newText;
                            txtRazonSocial.SelectionStart = selectionStart - 1;
                        }
                    }
                    else if (Teclado.IsFunctionKey(tecla, "Simbolo"))
                    {
                        _ventanaSimbolo = new AgregarSimbolo(_pantalla);
                        _ventanaSimbolo.ChildUpdated -= ProcesarSimbolo;
                        _ventanaSimbolo.ChildUpdated += ProcesarSimbolo;
                        _posicionSubV = Clases.Utiles.FrameworkElementPointToScreenPoint(txtRazonSocial);
                        _ventanaSimbolo.Top = _posicionSubV.Y;
                        _ventanaSimbolo.Left = _posicionSubV.X - (_ventanaSimbolo.Width / 2);
                        _ventanaSimbolo.Show();
                    }
                    else if (Teclado.IsNextPageKey(tecla))
                    {
                        if (txtRazonSocial.Text.Length < _maxNumeroCaracteresRazonSocial)
                        {
                            int selectionStart = txtRazonSocial.SelectionStart;
                            string currentText = txtRazonSocial.Text;

                            string newText = currentText.Insert(selectionStart, " ");

                            txtRazonSocial.Text = newText;
                            txtRazonSocial.SelectionStart = selectionStart + 1;
                        }
                    }
                    else if (Teclado.IsDecimalKey(tecla))
                    {
                        if (txtRazonSocial.Text.Length < _maxNumeroCaracteresRazonSocial)
                        {
                            int selectionStart = txtRazonSocial.SelectionStart;
                            string currentText = txtRazonSocial.Text;

                            string newText = currentText.Insert(selectionStart, ".");

                            txtRazonSocial.Text = newText;
                            txtRazonSocial.SelectionStart = selectionStart + 1;
                        }
                    }
                    else if (tecla == Key.F3)
                    {
                        if (txtRazonSocial.Text.Length < _maxNumeroCaracteresRazonSocial)
                        {
                            int selectionStart = txtRazonSocial.SelectionStart;
                            string currentText = txtRazonSocial.Text;

                            string newText = currentText.Insert(selectionStart, "Ñ");

                            txtRazonSocial.Text = newText;
                            txtRazonSocial.SelectionStart = selectionStart + 1;
                        }
                    }
                    else if (tecla == Key.Oem5)
                    {
                        if (txtRazonSocial.Text.Length < _maxNumeroCaracteresRazonSocial)
                        {
                            int selectionStart = txtRazonSocial.SelectionStart;
                            string currentText = txtRazonSocial.Text;

                            string newText = currentText.Insert(selectionStart, "&");

                            txtRazonSocial.Text = newText;
                            txtRazonSocial.SelectionStart = selectionStart + 1;
                        }
                    }
                    else if (tecla == Key.OemMinus)
                    {
                        if (txtRazonSocial.Text.Length < _maxNumeroCaracteresRazonSocial)
                        {
                            int selectionStart = txtRazonSocial.SelectionStart;
                            string currentText = txtRazonSocial.Text;

                            string newText = currentText.Insert(selectionStart, "-");

                            txtRazonSocial.Text = newText;
                            txtRazonSocial.SelectionStart = selectionStart + 1;
                        }
                    }
                    else if (tecla == Key.OemComma)
                    {
                        if (txtRazonSocial.Text.Length < _maxNumeroCaracteresRazonSocial)
                        {
                            int selectionStart = txtRazonSocial.SelectionStart;
                            string currentText = txtRazonSocial.Text;

                            string newText = currentText.Insert(selectionStart, ",");

                            txtRazonSocial.Text = newText;
                            txtRazonSocial.SelectionStart = selectionStart + 1;
                        }
                    }
                    else
                    {
                        if (txtRazonSocial.Text.Length < _maxNumeroCaracteresRazonSocial)
                        {
                            int selectionStart = txtRazonSocial.SelectionStart;
                            string currentText = txtRazonSocial.Text;

                            string newText = currentText.Insert(selectionStart, Teclado.GetKeyAlphaNumericValue(tecla).ToString());

                            txtRazonSocial.Text = newText;
                            txtRazonSocial.SelectionStart = selectionStart + 1;
                        }
                    }
                }
                else if (_enmMenuFactura == enmMenuFactura.IngresoNuevaRazonSocial)
                {
                    if (Teclado.IsConfirmationKey(tecla))
                    {
                        _razonSocialIngresada = txtRazonSocial.Text;
                        _rucIngresado = txtRuc.Text;
                        if (_razonSocialIngresada.Length == 0)
                        {
                            _pantalla.MensajeDescripcion(msgNuevoCliente);
                            _enmMenuFactura = enmMenuFactura.IngresoNuevaRazonSocial;
                        }
                        else if(_razonSocialIngresada.Length < _minimaCantidadCaracteresRazonSocialParaAutocompletar)
                        {
                            _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgRazonSocialCorta),
                                _minimaCantidadCaracteresRazonSocialParaAutocompletar.ToString()),
                                false
                                );
                        }
                        else
                        {
                            _pantalla.MensajeDescripcion(msgConfirmaDatos);
                            SetTextoBotonesAceptarCancelar("Cash", "Escape");
                            Application.Current.Dispatcher.Invoke((Action)(() =>
                            {
                                btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                            }));
                            Application.Current.Dispatcher.Invoke((Action)(() =>
                            {
                                btnTarjeta.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                                btnTarjeta.Visibility = Visibility.Visible;
                            }));
                            _enmMenuFactura = enmMenuFactura.MuestroDatosRuc;
                        }
                    }
                    else if (Teclado.IsEscapeKey(tecla))
                    {
                        _razonSocialIngresada = txtRazonSocial.Text;

                        if (_razonSocialIngresada.Length == 0)
                        {
                            _enmMenuFactura = enmMenuFactura.IngresoRut;
                            _pantalla.MensajeDescripcion(msgIngresoRut);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                            FormatearTextBox(eTextBoxVentanaFactura.RazonSocial, false);
                            FormatearTextBox(eTextBoxVentanaFactura.Ruc);
                        }
                        else
                        {
                            CargarRazonSocialEnControl();
                        }
                    }
                    else if (Teclado.IsFunctionKey(tecla, "Simbolo"))
                    {
                        _ventanaSimbolo = new AgregarSimbolo(_pantalla);
                        _ventanaSimbolo.ChildUpdated -= ProcesarSimbolo;
                        _ventanaSimbolo.ChildUpdated += ProcesarSimbolo;
                        _posicionSubV = Clases.Utiles.FrameworkElementPointToScreenPoint(txtRazonSocial);
                        _ventanaSimbolo.Top = _posicionSubV.Y;
                        _ventanaSimbolo.Left = _posicionSubV.X - (_ventanaSimbolo.Width / 2);
                        _ventanaSimbolo.Show();
                    }
                    else if (Teclado.IsBackspaceKey(tecla))
                    {
                        if (txtRazonSocial.Text.Length > 0)
                            CargarRazonSocialEnControl(txtRazonSocial.Text.Remove(txtRazonSocial.Text.Length - 1));
                    }
                    else if (Teclado.IsNextPageKey(tecla))
                    {
                        if (txtRazonSocial.Text.Length < _maxNumeroCaracteresRazonSocial)
                            CargarRazonSocialEnControl(txtRazonSocial.Text + " ");
                    }
                    else if (Teclado.IsDecimalKey(tecla))
                    {
                        if (txtRazonSocial.Text.Length < _maxNumeroCaracteresRazonSocial)
                            CargarRazonSocialEnControl(txtRazonSocial.Text + ".");
                    }
                    else if (Teclado.IsEnieKey(tecla))
                    {
                        if (txtRazonSocial.Text.Length < _maxNumeroCaracteresRazonSocial)
                            CargarRazonSocialEnControl(txtRazonSocial.Text + "Ñ");
                    }
                    else
                    {
                        if (txtRazonSocial.Text.Length < _maxNumeroCaracteresRazonSocial)
                            CargarRazonSocialEnControl(txtRazonSocial.Text + Teclado.GetKeyAlphaNumericValue(tecla));
                    }
                }
                else if (_enmMenuFactura == enmMenuFactura.ConsultaDatos)
                {
                    if (Teclado.IsEscapeKey(tecla))
                    {
                        CargarClaveEnControl();
                        CargarRucEnControl();
                        CargarRazonSocialEnControl();

                        FormatearTextBox(eTextBoxVentanaFactura.Clave);
                        FormatearTextBox(eTextBoxVentanaFactura.RazonSocial, false);
                        FormatearTextBox(eTextBoxVentanaFactura.Ruc, false);

                        _enmMenuFactura = enmMenuFactura.IngresoClave;
                        _pantalla.MensajeDescripcion(msgIngresoClave);
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                    }
                }
                else if (_enmMenuFactura == enmMenuFactura.SeleccionCliente)
                {
                    if (Teclado.IsEscapeKey(tecla))
                    {
                        dataGridClientes.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            dataGridClientes.ItemsSource = null;
                            dataGridClientes.Items.Refresh();
                        }));
                        _enmMenuFactura = enmMenuFactura.IngresoRazonSocial;
                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            txtTeclaSiguiente.Text = string.Empty;
                        }));
                        _pantalla.MensajeDescripcion(msgIngresoRazonSocial);
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        FormatearTextBox(eTextBoxVentanaFactura.Opcion, false);

                        //Limpio datos
                        CargarClaveEnControl();
                        CargarRucEnControl();
                        CargarOpcionEnControl();
                        FormatearTextBox(eTextBoxVentanaFactura.RazonSocial);
                    }
                    else if (Teclado.IsNumericKey(tecla))
                    {
                        int teclaNumerica = Teclado.GetKeyNumericValue(tecla);

                        int opcion = 0;
                        int.TryParse(txtOpcion.Text, out opcion);

                        if (txtOpcion.Text.Length == 1) //Tengo un solo digito cargado en textbox
                        {
                            CargarOpcionEnControl(string.Format("{0}{1}", opcion, teclaNumerica));
                        }
                        else //Tengo dos digitos cargados en textbox
                        {
                            // Borro dos digitos de texbox e ingreso nuevo digito
                            CargarOpcionEnControl(string.Format("{0}", teclaNumerica));
                        }
                    }
                    else if (Teclado.IsBackspaceKey(tecla))
                    {
                        if (txtOpcion.Text.Length > 0)
                        {
                            CargarOpcionEnControl(txtOpcion.Text.Remove(txtOpcion.Text.Length - 1));
                        }
                    }
                    else if (Teclado.IsConfirmationKey(tecla))
                    {
                        int opcion = 0;
                        int.TryParse(txtOpcion.Text, out opcion);

                        if (EstaEnRangoDePaginaVisible(opcion))
                        {
                            _itemSeleccionado = opcion;
                            ResaltarClienteSeleccionado(_itemSeleccionado);
                            if (dataGridClientes.SelectedItem != null)
                            {
                                _infoCliente = (dataGridClientes.SelectedItem as ClienteConIndex).InfoCliente;
                                _enmMenuFactura = enmMenuFactura.MuestroDatosRazonSocial;
                                _pantalla.MensajeDescripcion(msgConfirmaDatos);
                                SetTextoBotonesAceptarCancelar("Cash", "Escape");
                                Application.Current.Dispatcher.Invoke((Action)(() =>
                                {
                                    btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                                }));


                                Application.Current.Dispatcher.Invoke((Action)(() =>
                                {
                                    btnTarjeta.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                                    btnTarjeta.Visibility = Visibility.Visible;
                                }));

                                //btnTarjeta.Dispatcher.Invoke(() =>
                                //{
                                //    btnTarjeta.Visibility = Visibility.Visible;
                                //});
                                btnNext.Dispatcher.Invoke(() =>
                                {
                                    btnNext.Visibility = Visibility.Collapsed;
                                });
                                txtTeclaSiguiente.Dispatcher.BeginInvoke((Action)(() =>
                                {
                                    txtTeclaSiguiente.Text = "";
                                }));
                                CargarDatosEnControles(_infoCliente);
                            }
                        }
                    }
                    else if (Teclado.IsNextPageKey(tecla))
                    {
                        CargarListaClientesEnControl(_paginaVisible + 1);
                    }
                }
                else if (_enmMenuFactura == enmMenuFactura.MuestroDatosClave
                      || _enmMenuFactura == enmMenuFactura.MuestroDatosRazonSocial
                      || _enmMenuFactura == enmMenuFactura.MuestroDatosRuc)
                {
                    if (Teclado.IsCashKey(tecla) && _enmMenuFactura != enmMenuFactura.ConfirmarDatos)
                    {
                        EstadoFactura estadoFactura = new EstadoFactura();
                        _enmLastMenuFactura = _enmMenuFactura;
                        _enmMenuFactura = enmMenuFactura.ConfirmarDatos;
                        if (_listaClientes.Count == 0)
                        {
                            _infoCliente = new InfoCliente();
                            _infoCliente.Ruc = _rucIngresado;
                            _infoCliente.RazonSocial = _razonSocialIngresada;
                        }

                        estadoFactura.Codigo = eBusquedaFactura.Confirma;
                        Utiles.ClassUtiles.InsertarDatoVia(_infoCliente, ref listaDV);
                        Utiles.ClassUtiles.InsertarDatoVia(estadoFactura, ref listaDV);

                        EnviarDatosALogica(enmStatus.Ok, enmAccion.FACTURA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        _pantalla.MensajeDescripcion(string.Empty);
                        //_pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                    else if (Teclado.IsFunctionKey(tecla, "TarjetaCredito") && _enmMenuFactura != enmMenuFactura.ConfirmarDatos)
                    {
                        EstadoFactura estadoFactura = new EstadoFactura();
                        _enmLastMenuFactura = _enmMenuFactura;
                        _enmMenuFactura = enmMenuFactura.ConfirmarDatos;
                        if (_listaClientes.Count == 0)
                        {
                            _infoCliente = new InfoCliente();
                            _infoCliente.Ruc = _rucIngresado;
                            _infoCliente.RazonSocial = _razonSocialIngresada;
                        }

                        estadoFactura.Codigo = eBusquedaFactura.Confirma;
                        Utiles.ClassUtiles.InsertarDatoVia(_infoCliente, ref listaDV);
                        Utiles.ClassUtiles.InsertarDatoVia(estadoFactura, ref listaDV);

                        EnviarDatosALogica(enmStatus.Ok, enmAccion.FACTURA_TARJ, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        _pantalla.MensajeDescripcion(string.Empty);
                        //_pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                    else if (Teclado.IsEscapeKey(tecla))
                    {
                        Application.Current.Dispatcher.Invoke((Action)(() =>
                        {
                            btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyle);
                        }));
                        if (_enmMenuFactura == enmMenuFactura.MuestroDatosClave)
                        {
                            _enmMenuFactura = enmMenuFactura.IngresoClave;
                            _pantalla.MensajeDescripcion(msgIngresoClave);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");

                            btnTarjeta.Dispatcher.Invoke(() =>
                            {
                                btnTarjeta.Visibility = Visibility.Collapsed;
                            });

                            //Limpio info de los textboxs
                            CargarRucEnControl();
                            CargarRazonSocialEnControl();
                        }
                        else if (_enmMenuFactura == enmMenuFactura.MuestroDatosRazonSocial)
                        {
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                            btnTarjeta.Dispatcher.Invoke(() =>
                            {
                                btnTarjeta.Visibility = Visibility.Collapsed;
                            });

                            if (_cantidadClientes == 1) //Vuelvo al input de razon social
                            {
                                _enmMenuFactura = enmMenuFactura.IngresoRazonSocial;
                                _pantalla.MensajeDescripcion(msgIngresoRazonSocial);

                                //Limpio info de los textboxs
                                CargarRucEnControl();
                                CargarClaveEnControl();
                                CargarOpcionEnControl();

                                FormatearTextBox(eTextBoxVentanaFactura.Opcion, false);
                                FormatearTextBox(eTextBoxVentanaFactura.RazonSocial);
                            }
                            else //Vuelvo a estado seleccion de cliente en lista
                            {
                                _enmMenuFactura = enmMenuFactura.SeleccionCliente;
                                _pantalla.MensajeDescripcion(string.Empty);
                                if (_cantidadClientes > 10)
                                {
                                    txtTeclaSiguiente.Dispatcher.BeginInvoke((Action)(() =>
                                    {
                                        txtTeclaSiguiente.Text = string.Format(Traduccion.Traducir(
                                            msgSeleccionClienteMultiplesPaginas), Teclado.GetEtiquetaTecla("NextPage"));
                                    }));
                                }
                                ResaltarClienteSeleccionado(-1);
                                //Limpio info de los textboxs
                                CargarRucEnControl();
                                CargarClaveEnControl();
                            }
                        }
                        else if (_enmMenuFactura == enmMenuFactura.MuestroDatosRuc)
                        {
                            _enmMenuFactura = enmMenuFactura.IngresoRut;
                            _pantalla.MensajeDescripcion(msgIngresoRut);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                            btnTarjeta.Dispatcher.Invoke(() =>
                            {
                                btnTarjeta.Visibility = Visibility.Collapsed;
                            });

                            FormatearTextBox(eTextBoxVentanaFactura.RazonSocial, false);
                            FormatearTextBox(eTextBoxVentanaFactura.Ruc);
                            CargarRazonSocialEnControl();
                            CargarClaveEnControl();
                        }
                    }
                }
            }
        }
        #endregion

        #region Metodo de carga de textboxes de datos
        private void CargarDatosEnControles(InfoCliente infocliente = null)
        {
            try
            {
                CargarClaveEnControl(infocliente?.Clave.ToString());
                CargarRucEnControl(infocliente?.Ruc);
                CargarRazonSocialEnControl(infocliente?.RazonSocial);
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaCobroFactura:CargarDatosEnControles() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaCobroFactura:CargarDatosEnControles() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

        #region Metodo de carga de textboxes de clave
        private void CargarClaveEnControl(string clave = null)
        {
            try
            {
                txtClave.Dispatcher.Invoke((Action)(() =>
                {
                    if (clave == null)
                        txtClave.Text = string.Empty;
                    else
                        txtClave.Text = clave.ToString();
                }));
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaCobroFactura:CargarClaveEnControl() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaCobroFactura:CargarClaveEnControl() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

        #region Metodo de carga de textboxes de ruc
        private void CargarRucEnControl(string ruc = null)
        {
            try
            {
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    if (ruc == null)
                        txtRuc.Text = string.Empty;
                    else
                        txtRuc.Text = ruc;
                }));
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaCobroFactura:CargarRucEnControl() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaCobroFactura:CargarRucEnControl() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

        #region Metodo de carga de textboxes de RazonSocial
        private void CargarRazonSocialEnControl(string razonSocial = null)
        {
            try
            {
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    if (razonSocial == null)
                        txtRazonSocial.Text = string.Empty;
                    else
                        txtRazonSocial.Text = razonSocial;
                }));
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaCobroFactura:CargarRazonSocialEnControl() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaCobroFactura:CargarRazonSocialEnControl() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

        #region Metodo de carga de textbox de Opcion
        private void CargarOpcionEnControl(string opcion = null)
        {
            try
            {
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    if (opcion == null)
                        txtOpcion.Text = string.Empty;
                    else
                        txtOpcion.Text = opcion;
                }));
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaCobroFactura:CargarOpcionEnControl() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaCobroFactura:CargarOpcionEnControl() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

        #region Metodos de carga de lista de clientes

        /// <summary>
        /// Verifica si opcion ingresada se encuentra dentro del rango de la pagina de clientes visible en pantalla
        /// </summary>
        /// <returns></returns>
        private bool EstaEnRangoDePaginaVisible(int num)
        {
            bool ret = false;
            ret = _listaClientesIndex.Exists(c => c.Pagina == _paginaVisible && c.NumeroCliente == num);
            return ret;
        }

        /// <summary>
        /// Resalta el cliente seleccionada en la lista del control
        /// </summary>
        /// <param name="num"></param>
        /// <returns></returns>
        private void ResaltarClienteSeleccionado(int num)
        {
            int index = 0;

            if (num == -1)
                index = -1;
            else
            {
                if (num <= _filasVisiblesPrimerPaginaClientes)
                {
                    index = num - 1;
                }
                else
                {
                    index = (num - 1) % _filasVisiblesPrimerPaginaClientes;
                }
            }

            dataGridClientes.Dispatcher.Invoke((Action)(() =>
            {
                dataGridClientes.SelectedIndex = index;
                if(dataGridClientes.SelectedItem != null)
                    dataGridClientes.ScrollIntoView(dataGridClientes.SelectedItem);
            }));
        }

        private List<ClienteConIndex> MapearListaClientes(List<InfoCliente> listaInfocliente)
        {
            List<ClienteConIndex> lista = new List<ClienteConIndex>();
            int numeroCliente = 0;
            int numeroPagina = 0;

            foreach(InfoCliente ic in listaInfocliente)
            {
                numeroPagina = (numeroCliente / _filasVisiblesPrimerPaginaClientes ) + 1;
                lista.Add(new ClienteConIndex(ic, numeroCliente+1,numeroPagina) );
                numeroCliente++;
            }

            return lista;
        }

        private void CargarListaClientesEnControl(int numeroPagina = 1)
        {
            try
            {
                List<ClienteConIndex> lista = _listaClientesIndex.FindAll(c => c.Pagina == numeroPagina);

                if (lista.Count > 0)
                {
                    _paginaVisible = numeroPagina;
                    _pantalla.MensajeDescripcion(string.Empty);
                    if (_cantidadClientes > 10)
                    {
                        txtTeclaSiguiente.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            txtTeclaSiguiente.Text = string.Format(Traduccion.Traducir(
                                            msgSeleccionClienteMultiplesPaginas), Teclado.GetEtiquetaTecla("NextPage"));
                        }));
                        btnNext.Dispatcher.Invoke(() =>
                        {
                            btnNext.Visibility = Visibility.Visible;
                        });
                    }

                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        dataGridClientes.ItemsSource = lista;
                        dataGridClientes.Items.Refresh();
                        //dataGridClientes.SelectedIndex = 0;
                    }));
                }
                else
                {
                    CargarListaClientesEnControl(1);   //Se vuelve a llamar a la primer pag. si se llega al final.
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaTagManual:CargarDatosEnControles() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaTagManual:CargarDatosEnControles() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

        #region Formato textboxs

        private enum eTextBoxVentanaFactura { Clave, Ruc, RazonSocial, Opcion }

        /// <summary>
        /// Formatea colores de textbox
        /// </summary>
        /// <param name="eTextBox"></param>
        /// <param name="resaltar"></param>
        private void FormatearTextBox(eTextBoxVentanaFactura eTextBox, bool resaltar = true)
        {
            TextBox textbox = txtClave;

            switch (eTextBox)
            {
                case eTextBoxVentanaFactura.Clave:
                    textbox = txtClave;
                    break;
                case eTextBoxVentanaFactura.RazonSocial:
                    textbox = txtRazonSocial;
                    break;
                case eTextBoxVentanaFactura.Ruc:
                    textbox = txtRuc;
                    break;
                case eTextBoxVentanaFactura.Opcion:
                    textbox = txtOpcion;
                    break;
            }

            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (resaltar)
                {
                    textbox.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                }
                else
                {
                    textbox.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                }
            }));
        }
        #endregion

        #region Metodos de comunicacion (recepcion) con el modulo de logica
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
                        if (causa.Codigo == eCausas.Salidavehiculo && _enmMenuFactura == enmMenuFactura.ConfirmarDatos)
                            return bRet;
                        //Logica indica que se debe cerrar la ventana
                        this.Dispatcher.Invoke((Action)(() =>
                        {
                            _pantalla.MensajeDescripcion(string.Empty);
                            _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        }));
                    }
                }
                else if (comandoJson.Accion == enmAccion.FACTURA && comandoJson.Operacion != string.Empty)
                {
                    EstadoFactura estadoFactura = Utiles.ClassUtiles.ExtraerObjetoJson<EstadoFactura>(comandoJson.Operacion);

                    if (_enmMenuFactura == enmMenuFactura.ConsultaDatos)
                    {                                               
                        if (estadoFactura.Codigo == eBusquedaFactura.BusquedaNumeroCliente)
                        {
                            _listaClientes = Utiles.ClassUtiles.ExtraerObjetoJson<List<InfoCliente>>(comandoJson.Operacion);
                            _listaClientes.Sort((x, y) => x.RazonSocial.CompareTo(y.RazonSocial));
                            _cantidadClientes = _listaClientes.Count;
                            _listaClientesIndex = MapearListaClientes(_listaClientes);

                            if (_cantidadClientes == 0)
                            {
                                _pantalla.MensajeDescripcion(msgClienteNoHallado);
                                _enmMenuFactura = enmMenuFactura.IngresoClave;
                            }
                            else if (_cantidadClientes == 1)
                            {
                                CargarRazonSocialEnControl(_listaClientes[0].RazonSocial);
                                CargarRucEnControl(_listaClientes[0].Ruc);
                                _pantalla.MensajeDescripcion(msgConfirmaDatos);
                                SetTextoBotonesAceptarCancelar("Cash", "Escape");
                                btnAceptar.Dispatcher.Invoke((Action)(() =>
                                {
                                    btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                                }));

                                btnAceptar.Dispatcher.Invoke((Action)(() =>
                                {
                                    btnTarjeta.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                                    btnTarjeta.Visibility = Visibility.Visible;
                                }));

                                _enmMenuFactura = enmMenuFactura.MuestroDatosClave;
                                _infoCliente = _listaClientes[0];
                                //btnTarjeta.Dispatcher.Invoke(() =>
                                //{
                                //    btnTarjeta.Visibility = Visibility.Visible;
                                //});
                                btnNext.Dispatcher.Invoke(() =>
                                {
                                    btnNext.Visibility = Visibility.Collapsed;
                                });
                                txtTeclaSiguiente.Dispatcher.BeginInvoke((Action)(() =>
                                {
                                    txtTeclaSiguiente.Text = "";
                                }));
                            }
                            else
                            {
                                CargarListaClientesEnControl();
                                _enmMenuFactura = enmMenuFactura.SeleccionCliente;
                                FormatearTextBox(eTextBoxVentanaFactura.Clave,false);
                                FormatearTextBox(eTextBoxVentanaFactura.Opcion);
                            }
                        }
                        else if (estadoFactura.Codigo == eBusquedaFactura.BusquedaRut)
                        {
                            _listaClientes = Utiles.ClassUtiles.ExtraerObjetoJson<List<InfoCliente>>(comandoJson.Operacion);
                            _listaClientes.Sort((x, y) => x.RazonSocial.CompareTo(y.RazonSocial));
                            _cantidadClientes = _listaClientes.Count;
                            _listaClientesIndex = MapearListaClientes(_listaClientes);

                            if (_cantidadClientes == 0)
                            {
                                _pantalla.MensajeDescripcion(msgNuevoCliente);
                                _enmMenuFactura = enmMenuFactura.IngresoRazonSocial;
                                FormatearTextBox(eTextBoxVentanaFactura.Ruc, false);
                                FormatearTextBox(eTextBoxVentanaFactura.RazonSocial);
                            }
                            else if (_cantidadClientes == 1)
                            {
                                _pantalla.MensajeDescripcion(msgConfirmaDatos);
                                _enmMenuFactura = enmMenuFactura.MuestroDatosRuc;
                                SetTextoBotonesAceptarCancelar("Cash", "Escape");
                                CargarClaveEnControl(_listaClientes[0].Clave.ToString());
                                CargarRazonSocialEnControl(_listaClientes[0].RazonSocial);
                                CargarRucEnControl(_listaClientes[0].Ruc);
                                _infoCliente = _listaClientes[0];
                                btnAceptar.Dispatcher.Invoke((Action)(() =>
                                {
                                    btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                                }));
                                btnTarjeta.Dispatcher.Invoke(() =>
                                {
                                    btnTarjeta.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                                    btnTarjeta.Visibility = Visibility.Visible;
                                });
                                btnNext.Dispatcher.Invoke(() =>
                                {
                                    btnNext.Visibility = Visibility.Collapsed;
                                });
                                txtTeclaSiguiente.Dispatcher.BeginInvoke((Action)(() =>
                                {
                                    txtTeclaSiguiente.Text = "";
                                }));
                            }
                            else
                            {
                                CargarListaClientesEnControl();
                                _enmMenuFactura = enmMenuFactura.SeleccionCliente;
                                FormatearTextBox(eTextBoxVentanaFactura.Ruc, false);
                                FormatearTextBox(eTextBoxVentanaFactura.Opcion);
                            }
                        }
                        else if (estadoFactura.Codigo == eBusquedaFactura.BusquedaNombre)
                        {
                            _listaClientes = Utiles.ClassUtiles.ExtraerObjetoJson<List<InfoCliente>>(comandoJson.Operacion);
                            _listaClientes.Sort((x, y) => x.RazonSocial.CompareTo(y.RazonSocial));
                            _cantidadClientes = _listaClientes.Count;
                            _listaClientesIndex = MapearListaClientes(_listaClientes);

                            if (_cantidadClientes == 0)
                            {
                                _pantalla.MensajeDescripcion(msgClienteNoHallado);
                                _enmMenuFactura = enmMenuFactura.IngresoNuevaRazonSocial;
                                _pantalla.MensajeDescripcion("Confirme los datos para cobrar", false, 2);
                            }
                            else if (_cantidadClientes == 1)
                            {
                                CargarClaveEnControl(_listaClientes[0].Clave.ToString());
                                CargarRazonSocialEnControl(_listaClientes[0].RazonSocial);
                                CargarRucEnControl(_listaClientes[0].Ruc);
                                _pantalla.MensajeDescripcion(msgConfirmaDatos);
                                SetTextoBotonesAceptarCancelar("Cash", "Escape");
                                btnAceptar.Dispatcher.Invoke((Action)(() =>
                                {
                                    btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                                }));

                                btnAceptar.Dispatcher.Invoke((Action)(() =>
                                {
                                    btnTarjeta.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                                    btnTarjeta.Visibility = Visibility.Visible;
                                }));

                                _enmMenuFactura = enmMenuFactura.MuestroDatosRazonSocial;
                                _infoCliente = _listaClientes[0];
                                //btnTarjeta.Dispatcher.Invoke(() =>
                                //{
                                //    btnTarjeta.Visibility = Visibility.Visible;
                                //});
                                btnNext.Dispatcher.Invoke(() =>
                                {
                                    btnNext.Visibility = Visibility.Collapsed;
                                });
                                txtTeclaSiguiente.Dispatcher.BeginInvoke((Action)(() =>
                                {
                                    txtTeclaSiguiente.Text = "";
                                }));
                            }
                            else
                            {
                                CargarListaClientesEnControl();
                                _enmMenuFactura = enmMenuFactura.SeleccionCliente;
                                FormatearTextBox(eTextBoxVentanaFactura.RazonSocial, false);
                                FormatearTextBox(eTextBoxVentanaFactura.Opcion);
                            }
                        }
                        
                    }
                    else if(estadoFactura.Codigo == eBusquedaFactura.BusquedaPatente)
                    {
                        _listaClientes = Utiles.ClassUtiles.ExtraerObjetoJson<List<InfoCliente>>(comandoJson.Operacion);
                        CargarClaveEnControl(_listaClientes[0].Clave.ToString());
                        CargarRazonSocialEnControl(_listaClientes[0].RazonSocial);
                        CargarRucEnControl(_listaClientes[0].Ruc);
                        _pantalla.MensajeDescripcion(msgConfirmaDatos);
                        SetTextoBotonesAceptarCancelar("Cash", "Escape");
                        btnAceptar.Dispatcher.Invoke((Action)(() =>
                        {
                            btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                        }));

                        btnAceptar.Dispatcher.Invoke((Action)(() =>
                        {
                            btnTarjeta.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                            btnTarjeta.Visibility = Visibility.Visible;
                        }));

                        _enmMenuFactura = enmMenuFactura.MuestroDatosRazonSocial;
                        _infoCliente = _listaClientes[0];
                        //btnTarjeta.Dispatcher.Invoke(() =>
                        //{
                        //    btnTarjeta.Visibility = Visibility.Visible;
                        //});
                        btnNext.Dispatcher.Invoke(() =>
                        {
                            btnNext.Visibility = Visibility.Collapsed;
                        });
                        txtTeclaSiguiente.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            txtTeclaSiguiente.Text = "";
                        }));
                    }
                }
                else if (comandoJson.Accion == enmAccion.ESTADO_SUB &&
                        (comandoJson.CodigoStatus == enmStatus.FallaCritica || comandoJson.CodigoStatus == enmStatus.Ok))
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }));
                }
                else if(comandoJson.Accion == enmAccion.ESTADO_SUB && comandoJson.CodigoStatus == enmStatus.Error)
                {
                    if (_enmMenuFactura == enmMenuFactura.ConfirmarDatos)
                    {
                        _enmMenuFactura = _enmLastMenuFactura;
                        SetTextoBotonesAceptarCancelar("Cash", "Escape");                        
                        btnAceptar.Dispatcher.Invoke((Action)(() =>
                        {
                            btnAceptar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonCashStyle);
                        }));
                    }
                    else
                        bRet = true;
                }
                else if (comandoJson.Accion == enmAccion.DATOS_VEHICULO && comandoJson.CodigoStatus == enmStatus.Ok)
                {
                    // Agrego esto aca para evitar que una actualizacion de los datos del vehiculo desde logica 
                    // me pise los datos locales.
                    bRet = false;
                }
                else
                    bRet = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaFactura:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al recibir una Respuesta de logica.");
            }

            return bRet;
        }
        #endregion

        private void Border_Loaded(object sender, RoutedEventArgs e)
        {
            SetTextoBotonesAceptarCancelar("Enter", "Escape");
            Clases.Utiles.TraducirControles<TextBlock>(gridIngresoSistema);
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                _pantalla.MensajeDescripcion(msgIngresoClave);
                FormatearTextBox(eTextBoxVentanaFactura.Clave);
            }));
        }

        private void ProcesarSimbolo(Opcion item)
        {
            _ventanaSimbolo.Close();
            if (txtRazonSocial.Text.Length < _maxNumeroCaracteresRazonSocial)
            {
                string sNuevoSimb = item == null ? string.Empty : item?.Descripcion;
                CargarRazonSocialEnControl(txtRazonSocial.Text + sNuevoSimb);
            }
        }

        #region Pantalla Tactil

        private void CLAVE_Click(object sender, MouseButtonEventArgs e)
        {
            _enmMenuFactura = enmMenuFactura.IngresoClave;
            FormatearTextBox(eTextBoxVentanaFactura.Clave);
            FormatearTextBox(eTextBoxVentanaFactura.Ruc, false);
            FormatearTextBox(eTextBoxVentanaFactura.RazonSocial, false);
            _pantalla.MensajeDescripcion(msgIngresoClave);
            _pantalla.TecladoVisible();
            int position = txtClave.GetCharacterIndexFromPoint(e.GetPosition(txtClave), true);
            txtClave.Select(position, 0);
        }

        private void RUC_Click(object sender, MouseButtonEventArgs e)
        {
            _enmMenuFactura = enmMenuFactura.IngresoRut;
            FormatearTextBox(eTextBoxVentanaFactura.Clave, false);
            FormatearTextBox(eTextBoxVentanaFactura.Ruc);
            FormatearTextBox(eTextBoxVentanaFactura.RazonSocial, false);
            _pantalla.MensajeDescripcion(msgIngresoRut);
            _pantalla.TecladoVisible();
            int position = txtClave.GetCharacterIndexFromPoint(e.GetPosition(txtRuc), true);
            txtRuc.Select(position, 0);
        }

        private void RAZON_Click(object sender, MouseButtonEventArgs e)
        {
            if(_rucIngresado != string.Empty)
            {
                if (Clases.Utiles.ValidaRucPeru(_rucIngresado))
                {
                    _enmMenuFactura = enmMenuFactura.IngresoRazonSocial;
                    FormatearTextBox(eTextBoxVentanaFactura.Clave, false);
                    FormatearTextBox(eTextBoxVentanaFactura.Ruc, false);
                    FormatearTextBox(eTextBoxVentanaFactura.RazonSocial);
                    _pantalla.MensajeDescripcion(msgIngresoRazonSocial);
                    _pantalla.TecladoVisible();
                    int position = txtClave.GetCharacterIndexFromPoint(e.GetPosition(txtRazonSocial), true);
                    txtRazonSocial.Select(position, 0);
                }
                else
                {
                    _pantalla.MensajeDescripcion(msgIngresoRutInvalido);
                }
            }
            else
            {
                _enmMenuFactura = enmMenuFactura.IngresoRazonSocial;
                FormatearTextBox(eTextBoxVentanaFactura.Clave, false);
                FormatearTextBox(eTextBoxVentanaFactura.Ruc, false);
                FormatearTextBox(eTextBoxVentanaFactura.RazonSocial);
                _pantalla.MensajeDescripcion(msgIngresoRazonSocial);
                _pantalla.TecladoVisible();
                int position = txtClave.GetCharacterIndexFromPoint(e.GetPosition(txtRazonSocial), true);
                txtRazonSocial.Select(position, 0);
            }
            
        }

        private void txtRuc_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Establecer el foco en el TextBox
                textBox.Focus();
                if (textBox == txtClave)
                {
                    _enmMenuFactura = enmMenuFactura.IngresoClave;
                    FormatearTextBox(eTextBoxVentanaFactura.Clave);
                    FormatearTextBox(eTextBoxVentanaFactura.Ruc, false);
                    FormatearTextBox(eTextBoxVentanaFactura.RazonSocial, false);
                    _pantalla.MensajeDescripcion(msgIngresoClave);
                    _pantalla.TecladoVisible();
                }
                else if(textBox == txtRuc)
                {
                    _enmMenuFactura = enmMenuFactura.IngresoRut;
                    FormatearTextBox(eTextBoxVentanaFactura.Clave, false);
                    FormatearTextBox(eTextBoxVentanaFactura.Ruc);
                    FormatearTextBox(eTextBoxVentanaFactura.RazonSocial, false);
                    _pantalla.MensajeDescripcion(msgIngresoRut);
                    _pantalla.TecladoVisible();
                }
                else
                {
                    if (txtRuc.Text != string.Empty)
                    {
                        if (Clases.Utiles.ValidaRucPeru(txtRuc.Text))
                        {
                            _enmMenuFactura = enmMenuFactura.IngresoRazonSocial;
                            FormatearTextBox(eTextBoxVentanaFactura.Clave, false);
                            FormatearTextBox(eTextBoxVentanaFactura.Ruc, false);
                            FormatearTextBox(eTextBoxVentanaFactura.RazonSocial);
                            _pantalla.MensajeDescripcion(msgIngresoRazonSocial);
                            _pantalla.TecladoVisible();
                        }
                        else
                        {
                            _pantalla.MensajeDescripcion(msgIngresoRutInvalido);
                        }
                    }
                    else
                    {
                        _enmMenuFactura = enmMenuFactura.IngresoRazonSocial;
                        FormatearTextBox(eTextBoxVentanaFactura.Clave, false);
                        FormatearTextBox(eTextBoxVentanaFactura.Ruc, false);
                        FormatearTextBox(eTextBoxVentanaFactura.RazonSocial);
                        _pantalla.MensajeDescripcion(msgIngresoRazonSocial);
                        _pantalla.TecladoVisible();
                    }
                }

                // Obtener la posición del cursor en el TextBox
                Point position = e.GetPosition(textBox);
                int characterIndex = textBox.GetCharacterIndexFromPoint(position, true) + 1;

                // Establecer la selección en la posición del cursor
                textBox.Select(characterIndex, 0);

                // Establecer la posición y color del cursor en el TextBox
                textBox.CaretIndex = characterIndex;
                textBox.CaretBrush = new SolidColorBrush(Colors.White);
                textBox.IsReadOnlyCaretVisible = true;                
            }
        }

        private void ENTER_Click(object sender, RoutedEventArgs e)
        {
            List<DatoVia> listaDV = new List<DatoVia>();
            if (_enmMenuFactura == enmMenuFactura.MuestroDatosClave || 
                _enmMenuFactura == enmMenuFactura.MuestroDatosRazonSocial ||
                _enmMenuFactura == enmMenuFactura.MuestroDatosRuc)
            {
                EstadoFactura estadoFactura = new EstadoFactura();
                _enmLastMenuFactura = _enmMenuFactura;
                _enmMenuFactura = enmMenuFactura.ConfirmarDatos;
                _rucIngresado = txtRuc.Text;
                _razonSocialIngresada = txtRazonSocial.Text;
                if (_listaClientes.Count == 0)
                {
                    _infoCliente = new InfoCliente();
                    _infoCliente.Ruc = _rucIngresado;
                    _infoCliente.RazonSocial = _razonSocialIngresada;
                }

                estadoFactura.Codigo = eBusquedaFactura.Confirma;
                Utiles.ClassUtiles.InsertarDatoVia(_infoCliente, ref listaDV);
                Utiles.ClassUtiles.InsertarDatoVia(estadoFactura, ref listaDV);

                EnviarDatosALogica(enmStatus.Ok, enmAccion.FACTURA, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                _pantalla.MensajeDescripcion(string.Empty);
                //_pantalla.CargarSubVentana(enmSubVentana.Principal);                
            }
            else
            {
                ProcesarTecla(Key.Enter);
            }            
        }

        private void ESC_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(Key.Escape);
        }

        private void SIGUIENTE_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(Key.Space);
        }

        private void TARJETA_Click(object sender, RoutedEventArgs e)
        {
            List<DatoVia> listaDV = new List<DatoVia>();
            EstadoFactura estadoFactura = new EstadoFactura();
            //_enmLastMenuFactura = _enmMenuFactura;
            //_enmMenuFactura = enmMenuFactura.ConfirmarDatos;
            //if (_listaClientes.Count == 0)
            //{
            //    _infoCliente = new InfoCliente();
            //    _infoCliente.Ruc = _rucIngresado;
            //    _infoCliente.RazonSocial = _razonSocialIngresada;
            //}

            estadoFactura.Codigo = eBusquedaFactura.Confirma;
            Utiles.ClassUtiles.InsertarDatoVia(_infoCliente, ref listaDV);
            Utiles.ClassUtiles.InsertarDatoVia(estadoFactura, ref listaDV);

            EnviarDatosALogica(enmStatus.Ok, enmAccion.FACTURA_TARJ, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
            _pantalla.MensajeDescripcion(string.Empty);
            _pantalla.TecladoOculto();            
        }

        #endregion
    }
}
