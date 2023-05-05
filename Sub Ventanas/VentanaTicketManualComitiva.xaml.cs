using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Entidades;
using ModuloPantallaTeclado.Clases;
using Newtonsoft.Json;
using Entidades.Comunicacion;
using Entidades;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Controls;
using Utiles.Utiles;
using Utiles;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmTicketManualComitiva { IngresoHoraInicial, IngresoHoraFinal, IngresoCantidades, ConfirmoDatos }

    /// <summary>
    /// Lógica de interacción para VentanaTicketManual.xaml
    /// </summary>
    public partial class VentanaTicketManualComitiva : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmTicketManualComitiva _enmTicketManual;
        private string _nroTicketManual, _nroPtoVenta;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        private Causa _causa;
        private ListadoInfoTicketManual _listaTicketManual = null;
        private int _categoriaActual = 0;
        private int _cantidadCategoriasRecibidas = 0;
        private TimeSpan _horaInicial;
        private TimeSpan _horaFinal;
        private int _maximaCantidadDigitosTransito = 3;
        private const int _cantidadMaximaCategorias = 20;
        #endregion

        #region Mensajes de descripcion
        const string msgIngresoHoraInicial = "Ingrese la hora Inicial y presione {0}, {1} para volver.";
        const string msgIngresoHoraFinal = "Ingrese la hora Final y presione {0}, {1} para volver.";
        const string msgIngresoCantidad = "Ingrese cantidad y pase a la siguiente categoria con {0}";
        const string msgConfirmeDatos = "Confirme los datos ingresados con {0}";
        const string msgErrorHoraFinal = "La hora final debe ser MAYOR a la inicial";
        #endregion

        #region Constructor de la clase
        /// <summary>
        /// Constructor de la clase
        /// </summary>
        /// <param name="padre"></param>
        public VentanaTicketManualComitiva(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            Clases.Utiles.TraducirControles<TextBlock>(Grid_Principal);
            SetTextoBotonesAceptarCancelar("Enter", "Escape");
            _pantalla.TecladoVisible();

            if (_pantalla.ParametroAuxiliar != string.Empty)
            {
                _causa = ClassUtiles.ExtraerObjetoJson<Causa>(_pantalla.ParametroAuxiliar);
                lblTitulo.Dispatcher.Invoke((Action)(() => lblTitulo.Text = Traduccion.Traducir(_causa.Descripcion)));
                CargarDatosEnControles(_pantalla.ParametroAuxiliar);
                string strMsgAux = "";
                if(_causa.Codigo == eCausas.TicketManual)
                {
                    _enmTicketManual = enmTicketManualComitiva.IngresoHoraInicial;
                    strMsgAux = msgIngresoHoraInicial;
                }
                else if(_causa.Codigo == eCausas.ComitivaEfectivo || _causa.Codigo == eCausas.ComitivaExento)
                {
                    _enmTicketManual = enmTicketManualComitiva.IngresoCantidades;
                    FormatearTextBoxCantidad(_categoriaActual);
                    if (_causa.Codigo == eCausas.ComitivaEfectivo)
                    {
                        SetTextoBotonesAceptarCancelar("Cash", "Escape");
                        _pantalla.MensajeDescripcion(
                                    string.Format(Traduccion.Traducir(msgConfirmeDatos),
                                    Teclado.GetEtiquetaTecla("Cash")),
                                    false
                                    );
                    }
                    else if (_causa.Codigo == eCausas.ComitivaExento)
                    {
                        SetTextoBotonesAceptarCancelar("Exento", "Escape");
                        _pantalla.MensajeDescripcion(
                                    string.Format(Traduccion.Traducir(msgConfirmeDatos),
                                    Teclado.GetEtiquetaTecla("Exento")),
                                    false
                                    );
                    }
                    strMsgAux = msgIngresoCantidad;
                    gridHoraInicioFin.Dispatcher.Invoke((Action)(() => gridHoraInicioFin.Visibility = Visibility.Collapsed));
                }

                this.Dispatcher.Invoke((Action)(() =>
                {
                    txtHoraInicio.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                    _pantalla.MensajeDescripcion(
                                    string.Format(Traduccion.Traducir(strMsgAux),
                                    Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                    false
                                    );
                }));
            }

        }
        #endregion

        public void SetTextoBotonesAceptarCancelar(string TeclaConfirmacion, string TeclaCancelacion, bool BtnAceptarSiguiente = false)
        {
            this.Dispatcher.BeginInvoke((Action)(() =>
            {
                btnAceptar.Content = "Continuar" + " [" + Teclado.GetEtiquetaTecla(TeclaConfirmacion) + "]";
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
            FrameworkElement control = (FrameworkElement)borderTicketManual.Child;
            borderTicketManual.Child = null;
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
                    Causa causa = ClassUtiles.ExtraerObjetoJson<Causa>(comandoJson.Operacion);

                    if (causa.Codigo == eCausas.AperturaTurno
                        || causa.Codigo == eCausas.CausaCierre)
                    {
                        //Logica indica que se debe cerrar la ventana
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                }
                else if (comandoJson.Accion == enmAccion.ESTADO_SUB &&
                        (comandoJson.CodigoStatus == enmStatus.FallaCritica || comandoJson.CodigoStatus == enmStatus.Ok))
                {
                    this.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }));
                }
                else
                    bRet = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaTicketManual:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
                _logger.Debug("VentanaTicketManual:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar enviar datos a logica.");
            }
        }
        #endregion

        #region Metodo de carga de textboxes de datos
        private void CargarDatosEnControles(string datos)
        {
            try
            {
                _listaTicketManual = ClassUtiles.ExtraerObjetoJson<ListadoInfoTicketManual>(datos);
                _cantidadCategoriasRecibidas = _listaTicketManual.ListaTicketManual.Count;                

                for (int i = 0; i < _cantidadCategoriasRecibidas && i < _cantidadMaximaCategorias; i++)
                {
                    ActualizarTextBoxDescripcion(i, _listaTicketManual.ListaTicketManual[i].CategoriaDesc);
                    ActualizarTextBoxCantidad(i, true);
                    FormatearTextBoxCantidad(i, false);
                }
                for (int i = _cantidadCategoriasRecibidas; i < _cantidadMaximaCategorias; i++)
                {
                    ActualizarTextBoxDescripcion(i, string.Empty);
                    ActualizarTextBoxCantidad(i, false, 0, true);
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaTicketManual:CargarDatosEnControles() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaTicketManual:CargarDatosEnControles() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

        #region Metodos para acceder a textboxes
        /// <summary>
        /// Actualiza descripcion de denominacion
        /// </summary>
        /// <param name="codigoDenominacion"></param>
        /// <param name="descripcion"></param>
        private void ActualizarTextBoxDescripcion(int codigoCategoria, string descripcion)
        {
            TextBox textbox;
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                textbox = (TextBox)FindName(string.Format("textCategoria{0}", codigoCategoria));
                if (!string.IsNullOrEmpty(descripcion))
                {
                    if (!textbox.IsEnabled)
                        textbox.IsEnabled = true;
                    textbox.Text = descripcion;
                }
                else
                    textbox.Visibility = Visibility.Hidden;
            }));
        }

        /// <summary>
        /// Actualizo textbox de cantidad de cada denominacion
        /// </summary>
        /// <param name="codigoDenominacion"></param>
        /// <param name="limpiar"></param>
        ///     Limpia textbox
        /// <param name="digito"></param>
        ///     Digito a agregar (NO sumar). 
        ///     Valor por default elimina ultimo digito de textbox
        private void ActualizarTextBoxCantidad(int codigoCategoria, bool limpiar = false, int digito = -1, bool ocultar = false)
        {
            TextBox textbox;
            string cantidadActual = string.Empty;
            string cantidadNueva = string.Empty;

            textbox = (TextBox)FindName(string.Format("txtCantidadCategoria{0}", codigoCategoria));

            if (!ocultar)
            {
                cantidadActual = textbox.Text;

                if (limpiar)
                {
                    cantidadNueva = "0";
                }
                else
                {
                    cantidadNueva = cantidadActual;

                    if (digito == -1)
                    {
                        if (cantidadActual.Length > 1)
                        {
                            //Borro ultimo digito ingresado
                            cantidadNueva = string.Format("{0}", cantidadActual.Substring(0, cantidadActual.Length - 1));
                        }
                        else
                        {
                            cantidadNueva = "0";
                        }
                    }
                    else if (cantidadActual.Length < _maximaCantidadDigitosTransito)
                    {
                        //Agrego digito
                        cantidadNueva = string.Format("{0}{1}", cantidadActual == "0" ? "" : cantidadActual, digito.ToString("D1"));
                    }
                }

                if (cantidadActual != cantidadNueva)
                {
                    textbox.Dispatcher.Invoke((Action)(() =>
                    {
                        textbox.Text = cantidadNueva;
                    }));
                }
            }
            else
            {
                textbox.Dispatcher.Invoke((Action)(() =>
                {
                    textbox.Visibility = Visibility.Hidden;
                }));
            }
        }

        /// <summary>
        /// Cambia formato textbox de denominacion (resaltar o formato normal)
        /// </summary>
        /// <param name="codigoDenominacion"></param>
        /// <param name="resaltar"></param>
        private void FormatearTextBoxCantidad(int codigoDenominacion, bool resaltar = true)
        {
            TextBox textbox;
            textbox = (TextBox)FindName(string.Format("txtCantidadCategoria{0}", codigoDenominacion));

            this.Dispatcher.Invoke((Action)(() =>
            {
                if (!textbox.IsEnabled)
                    textbox.IsEnabled = true;
                if (resaltar)
                {
                    textbox.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                }
                else
                {
                    textbox.Style = Estilo.FindResource<Style>(ResourceList.TextBoxLiquidacionStyle);
                }
            }));
        }

        private void ValidarIngresoHoraTextBox(TextBox textbox, int key)
        {
            if(string.IsNullOrEmpty(textbox.Text))
            {
                if (key >= 0 && key <= 2)
                    textbox.Text += key.ToString();
            }
            else if(textbox.Text.Length == 1)
            {
                int primerDigito = int.Parse(textbox.Text);
                if (primerDigito == 0 || primerDigito == 1 || (primerDigito == 2 && key <=3))
                {
                    textbox.Text += key.ToString();
                    textbox.Text += ":";
                }
            }
            else if (textbox.Text.Length == 3)
            {
                if (key >= 0 && key <= 5)
                    textbox.Text += key.ToString();
            }
            else if (textbox.Text.Length == 4)
            {
                if (key >= 0 && key <= 9)
                    textbox.Text += key.ToString();
            }
        }

        private void BorrarUltimoIngresoHoraTextBox(TextBox textbox)
        {
            if (textbox.Text.Length > 0)
            {
                if (textbox.Text.Length == 3)  //Borro el ":"
                    textbox.Text = textbox.Text.Remove(textbox.Text.Length - 1);
                textbox.Text = textbox.Text.Remove(textbox.Text.Length - 1);
            }
        }

        private void MapearCantidadesPorCategoria()
        {
            TextBox textbox;

            for (int i = 0; i < _cantidadCategoriasRecibidas && i < _cantidadMaximaCategorias; i++)
            {
                textbox = (TextBox)FindName(string.Format("txtCantidadCategoria{0}", i));
                int cantidadCategoria = 0;
                if (int.TryParse(textbox.Text, out cantidadCategoria))
                {
                    _listaTicketManual.ListaTicketManual[i].Cantidad = cantidadCategoria;
                }
            }
        }
        #endregion

        #region Metodo de procesamiento de tecla recibida
        public void ProcesarTeclaUp(Key tecla)
        {
            if (Teclado.IsEscapeKey(tecla))
            {
                this.Dispatcher.Invoke((Action)(() =>
                {
                    btnCancelar.Style = Estilo.FindResource<Style>(ResourceList.ActionButtonStyle);
                }));
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                this.Dispatcher.Invoke((Action)(() =>
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
                if (_enmTicketManual == enmTicketManualComitiva.IngresoHoraInicial)
                {
                    EnviarDatosALogica(enmStatus.Abortada, enmAccion.TICKETMANUAL, string.Empty);
                    _pantalla.MensajeDescripcion(string.Empty);
                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                }
                else if (_enmTicketManual == enmTicketManualComitiva.IngresoHoraFinal)
                {
                    _enmTicketManual = enmTicketManualComitiva.IngresoHoraInicial;
                    txtHoraInicio.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                    txtHoraFin.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                    SetTextoBotonesAceptarCancelar("Enter", "Escape");
                    _pantalla.MensajeDescripcion(
                                    string.Format(Traduccion.Traducir(msgIngresoHoraInicial),
                                    Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                    false
                                    );
                }
                else if (_enmTicketManual == enmTicketManualComitiva.IngresoCantidades)
                {
                    FormatearTextBoxCantidad(_categoriaActual, false);
                    if (_categoriaActual == 0)
                    {
                        if (_causa.Codigo == eCausas.TicketManual)
                        {
                            ActualizarTextBoxCantidad(_categoriaActual, true);
                            _enmTicketManual = enmTicketManualComitiva.IngresoHoraFinal;
                            txtHoraFin.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                            _pantalla.MensajeDescripcion(
                                            string.Format(Traduccion.Traducir(msgIngresoHoraFinal),
                                            Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                            false
                                            );
                        }
                        else if (_causa.Codigo == eCausas.ComitivaEfectivo || _causa.Codigo == eCausas.ComitivaExento)
                        {
                            EnviarDatosALogica(enmStatus.Abortada, enmAccion.COMITIVA, string.Empty);
                            _pantalla.MensajeDescripcion(string.Empty);
                            _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        }
                    }
                    else
                    {
                        ActualizarTextBoxCantidad(_categoriaActual--, true);
                        FormatearTextBoxCantidad(_categoriaActual);
                    }
                }
                else if (_enmTicketManual == enmTicketManualComitiva.ConfirmoDatos)
                {
                    SetTextoBotonesAceptarCancelar("Enter", "Escape");
                    _pantalla.MensajeDescripcion(
                                    string.Format(Traduccion.Traducir(msgIngresoCantidad),
                                    Teclado.GetEtiquetaTecla("Enter")),
                                    false
                                    );
                    _enmTicketManual = enmTicketManualComitiva.IngresoCantidades;
                    FormatearTextBoxCantidad(_categoriaActual);
                }
            }
            else if (Teclado.IsCashKey(tecla))
            {
                if (_causa.Codigo == eCausas.ComitivaEfectivo || _causa.Codigo == eCausas.TicketManual)
                {
                    if (_enmTicketManual == enmTicketManualComitiva.ConfirmoDatos ||
                        (_enmTicketManual == enmTicketManualComitiva.IngresoCantidades && _categoriaActual > 1))
                    {
                        MapearCantidadesPorCategoria();
                        ClassUtiles.InsertarDatoVia(_causa, ref listaDV);
                        ClassUtiles.InsertarDatoVia(_listaTicketManual, ref listaDV);
                        EnviarDatosALogica(enmStatus.Ok, _causa.Codigo == eCausas.TicketManual ? enmAccion.TICKETMANUAL : enmAccion.COMITIVA,
                                           JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                }
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                if (_enmTicketManual == enmTicketManualComitiva.IngresoHoraInicial)
                {
                    if (!string.IsNullOrEmpty(txtHoraInicio.Text) && txtHoraInicio.Text.Length == 5)
                    {
                        _horaInicial = TimeSpan.Parse(txtHoraInicio.Text);
                        _enmTicketManual = enmTicketManualComitiva.IngresoHoraFinal;
                        txtHoraFin.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                        txtHoraInicio.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        _pantalla.MensajeDescripcion(
                                        string.Format(Traduccion.Traducir(msgIngresoHoraFinal),
                                        Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                        false
                                        );
                    }
                }
                else if (_enmTicketManual == enmTicketManualComitiva.IngresoHoraFinal)
                {
                    if (!string.IsNullOrEmpty(txtHoraFin.Text) && txtHoraFin.Text.Length == 5)
                    {
                        _horaFinal = TimeSpan.Parse(txtHoraFin.Text);
                        //Chequeo que la hora final sea > hora inicial
                        if (_horaFinal > _horaInicial)
                        {
                            _listaTicketManual.HoraDesde = _horaInicial;
                            _listaTicketManual.HoraHasta = _horaFinal;
                            _enmTicketManual = enmTicketManualComitiva.IngresoCantidades;
                            txtHoraFin.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                            FormatearTextBoxCantidad(_categoriaActual);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                            _pantalla.MensajeDescripcion(
                                            string.Format(Traduccion.Traducir(msgIngresoCantidad),
                                            Teclado.GetEtiquetaTecla("Enter")),
                                            false
                                            );
                        }
                        else
                        {
                            _pantalla.MensajeDescripcion(msgErrorHoraFinal);
                            txtHoraFin.Dispatcher.Invoke((Action)(() => txtHoraFin.Text = string.Empty));
                        }
                    }
                }
                else if (_enmTicketManual == enmTicketManualComitiva.IngresoCantidades)
                {
                    if (_categoriaActual >= _cantidadCategoriasRecibidas - 1
                      || _categoriaActual >= _cantidadMaximaCategorias - 1)
                    {
                        FormatearTextBoxCantidad(_categoriaActual, false);
                        _enmTicketManual = enmTicketManualComitiva.ConfirmoDatos;
                        if (_causa.Codigo == eCausas.ComitivaEfectivo)
                        {
                            SetTextoBotonesAceptarCancelar("Cash", "Escape");
                            _pantalla.MensajeDescripcion(
                                        string.Format(Traduccion.Traducir(msgConfirmeDatos),
                                        Teclado.GetEtiquetaTecla("Cash")),
                                        false
                                        );
                        }
                        else if (_causa.Codigo == eCausas.ComitivaExento)
                        {
                            SetTextoBotonesAceptarCancelar("Exento", "Escape");
                            _pantalla.MensajeDescripcion(
                                        string.Format(Traduccion.Traducir(msgConfirmeDatos),
                                        Teclado.GetEtiquetaTecla("Exento")),
                                        false
                                        );
                        }
                    }
                    else
                    {
                        if ((_categoriaActual + 1) <= _cantidadCategoriasRecibidas)
                        {
                            FormatearTextBoxCantidad(_categoriaActual, false);
                            _categoriaActual++;
                            FormatearTextBoxCantidad(_categoriaActual);

                            if(_causa.Codigo == eCausas.ComitivaEfectivo)
                            {
                                MapearCantidadesPorCategoria();
                                ClassUtiles.InsertarDatoVia(_causa, ref listaDV);
                                ClassUtiles.InsertarDatoVia(_listaTicketManual, ref listaDV);
                                EnviarDatosALogica(enmStatus.Ok, enmAccion.COMITIVA_PARCIAL,
                                                   JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                            }
                            
                        }
                    }
                }
            }
            else if (Teclado.IsBackspaceKey(tecla))
            {
                if (_enmTicketManual == enmTicketManualComitiva.IngresoHoraInicial)
                {
                    BorrarUltimoIngresoHoraTextBox(txtHoraInicio);
                }
                else if (_enmTicketManual == enmTicketManualComitiva.IngresoHoraFinal)
                {
                    BorrarUltimoIngresoHoraTextBox(txtHoraFin);
                }
                else if (_enmTicketManual == enmTicketManualComitiva.IngresoCantidades)
                {
                    ActualizarTextBoxCantidad(_categoriaActual);
                }
            }          
            else if (Teclado.IsFunctionKey(tecla, "Exento"))
            {
                if (_causa.Codigo == eCausas.ComitivaExento)
                {
                    if (_enmTicketManual == enmTicketManualComitiva.ConfirmoDatos ||
                        (_enmTicketManual == enmTicketManualComitiva.IngresoCantidades && _categoriaActual > 1))
                    {
                        MapearCantidadesPorCategoria();
                        ClassUtiles.InsertarDatoVia(_causa, ref listaDV);
                        ClassUtiles.InsertarDatoVia(_listaTicketManual, ref listaDV);
                        EnviarDatosALogica(enmStatus.Ok, enmAccion.COMITIVA,
                                           JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                }
            }
            else if (Teclado.IsNumericKey(tecla))
            {
                if (_enmTicketManual == enmTicketManualComitiva.IngresoHoraInicial)
                {
                    ValidarIngresoHoraTextBox(txtHoraInicio, Teclado.GetKeyNumericValue(tecla));
                }
                else if (_enmTicketManual == enmTicketManualComitiva.IngresoHoraFinal)
                {
                    ValidarIngresoHoraTextBox(txtHoraFin, Teclado.GetKeyNumericValue(tecla));
                }
                else if (_enmTicketManual == enmTicketManualComitiva.IngresoCantidades)
                {
                    ActualizarTextBoxCantidad(_categoriaActual, false, Teclado.GetKeyNumericValue(tecla));
                }
            }
            else if (_causa.Codigo == eCausas.ComitivaEfectivo || _causa.Codigo == eCausas.ComitivaExento)
            {
                if (_enmTicketManual == enmTicketManualComitiva.IngresoCantidades)
                {
                    string strCategoria = string.Empty;
                    for (int i = 0; i < _cantidadCategoriasRecibidas && i < _cantidadMaximaCategorias; i++)
                    {
                        strCategoria = string.Format("Categoria{0}", i + 1);
                        if (Teclado.IsFunctionKey(tecla, strCategoria))
                        {
                            FormatearTextBoxCantidad(_categoriaActual, false);
                            _categoriaActual = i;
                            FormatearTextBoxCantidad(_categoriaActual);
                            break;
                        }
                    }
                }
            }
        }
        #endregion

        private void CAT0_Click(object sender, RoutedEventArgs e)
        {
            _categoriaActual = 0;
            ActualizarTextBoxCantidad(0);
            FormatearTextBoxCantidad(0, true);
            _pantalla.TecladoVisible();
        }
        private void CAT1_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.TecladoVisible();
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 1;
            ActualizarTextBoxCantidad(1);
            FormatearTextBoxCantidad(1, true);   
        }
        private void CAT2_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 2;
            ActualizarTextBoxCantidad(2);
            FormatearTextBoxCantidad(2, true);
            _pantalla.TecladoVisible();
        }
        private void CAT3_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 3;
            ActualizarTextBoxCantidad(3);
            FormatearTextBoxCantidad(3, true);
            _pantalla.TecladoVisible();
        }
        private void CAT4_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 4;
            ActualizarTextBoxCantidad(4);
            FormatearTextBoxCantidad(4, true);
            _pantalla.TecladoVisible();
        }
        private void CAT5_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 5;
            ActualizarTextBoxCantidad(5);
            FormatearTextBoxCantidad(5, true);
            _pantalla.TecladoVisible();
        }
        private void CAT6_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 6;
            ActualizarTextBoxCantidad(6);
            FormatearTextBoxCantidad(6, true);
            _pantalla.TecladoVisible();
        }
        private void CAT7_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 7;
            ActualizarTextBoxCantidad(7);
            FormatearTextBoxCantidad(7, true);
            _pantalla.TecladoVisible();
        }
        private void CAT8_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 8;
            ActualizarTextBoxCantidad(8);
            FormatearTextBoxCantidad(8, true);
            _pantalla.TecladoVisible();
        }
        private void CAT9_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 9;
            ActualizarTextBoxCantidad(9);
            FormatearTextBoxCantidad(9, true);
            _pantalla.TecladoVisible();
        }
        private void CAT10_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 10;
            ActualizarTextBoxCantidad(10);
            FormatearTextBoxCantidad(10, true);
            _pantalla.TecladoVisible();
        }
        private void CAT11_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 11;
            ActualizarTextBoxCantidad(11);
            FormatearTextBoxCantidad(11, true);
            _pantalla.TecladoVisible();
        }
        private void CAT12_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 12;
            ActualizarTextBoxCantidad(12);
            FormatearTextBoxCantidad(12, true);
            _pantalla.TecladoVisible();
        }
        private void CAT13_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 13;
            ActualizarTextBoxCantidad(13);
            FormatearTextBoxCantidad(13, true);
            _pantalla.TecladoVisible();
        }
        private void CAT14_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 14;
            ActualizarTextBoxCantidad(14);
            FormatearTextBoxCantidad(14, true);
            _pantalla.TecladoVisible();
        }
        private void CAT15_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 15;
            ActualizarTextBoxCantidad(15);
            FormatearTextBoxCantidad(15, true);
            _pantalla.TecladoVisible();
        }
        private void CAT16_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 16;
            ActualizarTextBoxCantidad(16);
            FormatearTextBoxCantidad(16, true);
            _pantalla.TecladoVisible();
        }
        private void CAT17_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 17;
            ActualizarTextBoxCantidad(17);
            FormatearTextBoxCantidad(17, true);
            _pantalla.TecladoVisible();
        }
        private void CAT18_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 18;
            ActualizarTextBoxCantidad(18);
            FormatearTextBoxCantidad(18, true);
            _pantalla.TecladoVisible();
        }
        private void CAT19_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 19;
            ActualizarTextBoxCantidad(19);
            FormatearTextBoxCantidad(19, true);
            _pantalla.TecladoVisible();
        }
        private void CAT20_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 20; i++)
                FormatearTextBoxCantidad(i, false);
            _categoriaActual = 20;
            ActualizarTextBoxCantidad(20);
            FormatearTextBoxCantidad(20, true);
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

        private void CONFIRMAR_Click(object sender, RoutedEventArgs e)
        {
            List<DatoVia> listaDV = new List<DatoVia>();
            if (_causa.Codigo == eCausas.ComitivaEfectivo || _causa.Codigo == eCausas.TicketManual)
            {
                if (_enmTicketManual == enmTicketManualComitiva.ConfirmoDatos ||
                    (_enmTicketManual == enmTicketManualComitiva.IngresoCantidades && _categoriaActual >= 1))
                {
                    MapearCantidadesPorCategoria();
                    ClassUtiles.InsertarDatoVia(_causa, ref listaDV);
                    ClassUtiles.InsertarDatoVia(_listaTicketManual, ref listaDV);
                    EnviarDatosALogica(enmStatus.Ok, _causa.Codigo == eCausas.TicketManual ? enmAccion.TICKETMANUAL : enmAccion.COMITIVA,
                                       JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    _pantalla.MensajeDescripcion(string.Empty);
                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    _pantalla.TecladoOculto();
                }
            }

            if (_causa.Codigo == eCausas.ComitivaExento)
            {
                if (_enmTicketManual == enmTicketManualComitiva.ConfirmoDatos ||
                    (_enmTicketManual == enmTicketManualComitiva.IngresoCantidades && _categoriaActual >= 1))
                {
                    MapearCantidadesPorCategoria();
                    ClassUtiles.InsertarDatoVia(_causa, ref listaDV);
                    ClassUtiles.InsertarDatoVia(_listaTicketManual, ref listaDV);
                    EnviarDatosALogica(enmStatus.Ok, enmAccion.COMITIVA,
                                       JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    _pantalla.MensajeDescripcion(string.Empty);
                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    _pantalla.TecladoOculto();
                }
            }
        }
    }
}
