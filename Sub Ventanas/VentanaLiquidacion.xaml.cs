using Entidades.Logica;
using ModuloPantallaTeclado.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Entidades;
using Entidades.Comunicacion;
using ModuloPantallaTeclado.Clases;
using System.Windows.Media;
using System.Windows.Input;
using Utiles.Utiles;
using System.Diagnostics;
using System.IO;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmMenuLiquidacion { IngresoCantidades, IngresoBolsa, IngresoPrecinto, Confirmacion, IngresaObservacion }

    /// <summary>
    /// Lógica de interacción para VentanaLiquidacion.xaml
    /// </summary>
    public partial class VentanaLiquidacion : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmMenuLiquidacion _enmMenuLiquidacion = enmMenuLiquidacion.IngresoCantidades;
        private decimal _importeALiquidar = 0;
        private int _numeroDeBolsa = 0;
        private int _numeroDePrecinto = 0;
        private bool _usaBolsa = false;
        private int _cantidadMaximaDenominaciones = 15;
        private int _maximaCantidadDigitosDenominacion = 4;
        private int _maximaCantidadDigitosBolsa = 8;
        private int _maximaCantidadCaracteresObs = 45;
        private int _cantidadDenominacionesRecibidas = 0;
        private int _denominacionActual = 0;
        private Parte _parte;
        private ListadoDenominaciones _listaDenominaciones = null;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private AgregarSimbolo _ventanaSimbolo;
        private Point _posicionSubV;
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgIngresoCantidad = "Ingrese cantidad de denominacion y confirme";
        const string msgIngresoBolsa = "Ingrese el número de bolsa y confirme";
        const string msgIngresoPrecinto = "Ingrese el número de precinto y confirme";
        const string msgIngresoObservacion = "Ingrese la observación y confirme";
        const string msgConfirmeDatos = "Confirme los datos ingresados";
        #endregion

        #region Constructor de la clase
        public VentanaLiquidacion(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            LimpiarTextoTodosTextboxes();
            SetTextoBotonesAceptarCancelar("Enter", "Escape", true);
            Clases.Utiles.TraducirControles<TextBlock>(Grid_Principal);
            if (_pantalla.ParametroAuxiliar != string.Empty)
            {
                _usaBolsa = Utiles.ClassUtiles.ExtraerObjetoJson<Bolsa>(_pantalla.ParametroAuxiliar).UsaBolsa;
                CargarDatosEnControles(_pantalla.ParametroAuxiliar);
                FormatearTextBoxDenominacion(_denominacionActual);

                if (!_usaBolsa)
                {
                    lblNumeroBolsa.Visibility = Visibility.Collapsed;
                    txtBolsa.Visibility = Visibility.Collapsed;
                    lblNumeroPrecinto.Visibility = Visibility.Collapsed;
                    txtPrecinto.Visibility = Visibility.Collapsed;
                }
            }
            _pantalla.TecladoVisible();
            _posicionSubV = gridDenom.TransformToAncestor(Grid_Principal).Transform(new Point(0, 0));
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
            FrameworkElement control = (FrameworkElement)borderVentanaLiquidacion.Child;
            borderVentanaLiquidacion.Child = null;
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
                    _pantalla.MensajeDescripcion(msgIngresoObservacion);
                }
                else
                    _ventanaSimbolo.ProcesarTecla(tecla);
            }
            else
            {
                if (Teclado.IsEscapeKey(tecla))
                {
                    if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresoCantidades)
                    {
                        if (_denominacionActual == 0)
                        {
                            EnviarDatosALogica(enmStatus.Abortada, enmAccion.LIQUIDACION, string.Empty);
                            _pantalla.MensajeDescripcion(string.Empty);
                            _pantalla.CargarSubVentana(enmSubVentana.Principal);
                            _pantalla.TecladoOculto();
                        }
                        else
                        {
                            FormatearTextBoxDenominacion(_denominacionActual, false);
                            ActualizarTextBoxCantidad(_denominacionActual--, true);
                            FormatearTextBoxDenominacion(_denominacionActual);
                        }
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresoBolsa && _usaBolsa)
                    {
                        _pantalla.MensajeDescripcion(msgIngresoCantidad);
                        SetTextoBotonesAceptarCancelar("Enter", "Escape", true);
                        _enmMenuLiquidacion = enmMenuLiquidacion.IngresoCantidades;
                        ActualizarTextBoxBolsa("", true);
                        FormatearTextBoxDenominacion(_denominacionActual);
                        FormatearTextBoxBolsa(false);
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresoPrecinto && _usaBolsa)
                    {
                        _pantalla.MensajeDescripcion(msgIngresoBolsa);
                        _enmMenuLiquidacion = enmMenuLiquidacion.IngresoBolsa;
                        ActualizarTextBoxPrecinto("", true);
                        FormatearTextBoxPrecinto(false);
                        FormatearTextBoxBolsa();
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresaObservacion)
                    {
                        if (_usaBolsa)
                        {
                            _pantalla.MensajeDescripcion(msgIngresoPrecinto);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                            _enmMenuLiquidacion = enmMenuLiquidacion.IngresoPrecinto;
                            FormatearTextBoxPrecinto();
                        }
                        else
                        {
                            _pantalla.MensajeDescripcion(msgIngresoCantidad);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape", true);
                            _enmMenuLiquidacion = enmMenuLiquidacion.IngresoCantidades;
                            FormatearTextBoxDenominacion(_denominacionActual);
                        }

                        ActualizarTextBoxObservacion("", true);
                        FormatearTextBoxObservacion(false);
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.Confirmacion)
                    {
                        _pantalla.MensajeDescripcion(msgIngresoObservacion);
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        _enmMenuLiquidacion = enmMenuLiquidacion.IngresaObservacion;
                        FormatearTextBoxObservacion();
                    }
                }
                else if (Teclado.IsConfirmationKey(tecla))
                {
                    if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresoCantidades)
                    {
                        if (_denominacionActual >= _cantidadMaximaDenominaciones - 1
                          || _denominacionActual >= _cantidadDenominacionesRecibidas - 1)
                        {
                            if (_usaBolsa)
                            {
                                _pantalla.MensajeDescripcion(msgIngresoBolsa);
                                _enmMenuLiquidacion = enmMenuLiquidacion.IngresoBolsa;
                                FormatearTextBoxDenominacion(_denominacionActual, false);
                                FormatearTextBoxBolsa();
                            }
                            else
                            {
                                FormatearTextBoxDenominacion(_denominacionActual, false);
                                _pantalla.MensajeDescripcion(msgIngresoObservacion);
                                SetTextoBotonesAceptarCancelar("Enter", "Escape");
                                _enmMenuLiquidacion = enmMenuLiquidacion.IngresaObservacion;
                                FormatearTextBoxObservacion();
                            }
                        }
                        else
                        {
                            FormatearTextBoxDenominacion(_denominacionActual, false);
                            _denominacionActual++;
                            FormatearTextBoxDenominacion(_denominacionActual);
                        }
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresoBolsa && _usaBolsa)
                    {
                        FormatearTextBoxBolsa(false);
                        _pantalla.MensajeDescripcion(msgIngresoPrecinto);
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        _enmMenuLiquidacion = enmMenuLiquidacion.IngresoPrecinto;
                        FormatearTextBoxPrecinto();
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresoPrecinto && _usaBolsa)
                    {
                        FormatearTextBoxPrecinto(false);
                        _pantalla.MensajeDescripcion(msgIngresoObservacion);
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        _enmMenuLiquidacion = enmMenuLiquidacion.IngresaObservacion;
                        FormatearTextBoxObservacion();
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresaObservacion)
                    {
                        FormatearTextBoxObservacion(false);
                        _pantalla.MensajeDescripcion(msgConfirmeDatos);
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        _enmMenuLiquidacion = enmMenuLiquidacion.Confirmacion;
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.Confirmacion)
                    {
                        if (_usaBolsa)
                        {
                            if (txtBolsa.Text.Length > 0)
                                _numeroDeBolsa = int.Parse(txtBolsa.Text);
                            else
                                _numeroDeBolsa = 0;

                            if (txtPrecinto.Text.Length > 0)
                                _numeroDePrecinto = int.Parse(txtPrecinto.Text);
                            else
                                _numeroDePrecinto = 0;
                        }

                        _importeALiquidar = 0;
                        for (int i = 0; i < _cantidadDenominacionesRecibidas && i < _cantidadMaximaDenominaciones; i++)
                        {
                            _importeALiquidar += ObtenerImporteDenominacion(i);
                        }

                        Liquidacion liquidacion = new Liquidacion();
                        liquidacion.NumeroBolsa = _numeroDeBolsa;
                        liquidacion.NumeroPrecinto = _numeroDePrecinto;
                        liquidacion.Importe = _importeALiquidar;
                        liquidacion.Observacion = txtObservacion.Text;
                        liquidacion.ListaLiquidacionxDenominacion = MapearCantidadesPorDenominacion();

                        Utiles.ClassUtiles.InsertarDatoVia(liquidacion, ref listaDV);

                        EnviarDatosALogica(enmStatus.Ok, enmAccion.LIQUIDACION, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        _pantalla.MensajeDescripcion(string.Empty);
                        //_pantalla.CargarSubVentana(enmSubVentana.Principal);
                        _pantalla.TecladoOculto();
                    }
                }
                else if (Teclado.IsBackspaceKey(tecla))
                {
                    if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresoCantidades)
                    {
                        ActualizarTextBoxCantidad(_denominacionActual);
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresoBolsa && _usaBolsa)
                    {
                        ActualizarTextBoxBolsa();
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresoPrecinto && _usaBolsa)
                    {
                        ActualizarTextBoxPrecinto();
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresaObservacion)
                    {
                        ActualizarTextBoxObservacion();
                    }
                }
                else if (Teclado.IsFunctionKey(tecla, "Simbolo"))
                {
                    if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresaObservacion)
                    {
                        _ventanaSimbolo = new AgregarSimbolo(_pantalla);
                        _ventanaSimbolo.ChildUpdated -= ProcesarSimbolo;
                        _ventanaSimbolo.ChildUpdated += ProcesarSimbolo;
                        _ventanaSimbolo.Top = Math.Abs(_posicionSubV.Y - (Height / 2)) - txtObservacion.ActualHeight;
                        _ventanaSimbolo.Left = Width + ((txtObservacion.ActualWidth - _posicionSubV.X) / 4);
                        _ventanaSimbolo.Show();
                    }
                }
                else
                {
                    if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresoCantidades)
                    {
                        if (Teclado.IsNumericKey(tecla))
                        {
                            ActualizarTextBoxCantidad(_denominacionActual, false, Teclado.GetKeyNumericValue(tecla));
                        }
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresoBolsa && _usaBolsa)
                    {
                        if (Teclado.IsNumericKey(tecla))
                        {
                            ActualizarTextBoxBolsa(Teclado.GetKeyNumericValue(tecla).ToString());
                        }
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresoPrecinto && _usaBolsa)
                    {
                        if (Teclado.IsNumericKey(tecla))
                        {
                            ActualizarTextBoxPrecinto(Teclado.GetKeyNumericValue(tecla).ToString());
                        }
                    }
                    else if (_enmMenuLiquidacion == enmMenuLiquidacion.IngresaObservacion)
                    {
                        char? keyPressed = null;

                        if (Teclado.IsDecimalKey(tecla))
                            keyPressed = '.';
                        else
                            keyPressed = Teclado.GetKeyAlphaNumericValue(tecla);

                        // Para que no tome en cuenta las teclas especiales
                        if (keyPressed.HasValue)
                        {
                            txtObservacion.ScrollToEnd();
                            ActualizarTextBoxObservacion(keyPressed.ToString());
                        }
                        else if (Teclado.IsNextPageKey(tecla))
                            ActualizarTextBoxObservacion(" ");
                    }
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
                if (comandoJson.CodigoStatus == enmStatus.Ok
                    && comandoJson.Accion == enmAccion.ESTADO)
                {
                    Causa causa = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(comandoJson.Operacion);

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
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }));
                }
                else
                    bRet = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaLiquidacion:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
                _logger.Debug("VentanaLiquidacion:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar enviar datos a logica.");
            }
        }
        #endregion

        #region Metodo de carga de textboxes de datos
        private void CargarDatosEnControles(string datos)
        {
            try
            {
                _parte = Utiles.ClassUtiles.ExtraerObjetoJson<Parte>(datos);
                _listaDenominaciones= Utiles.ClassUtiles.ExtraerObjetoJson<ListadoDenominaciones>(datos);

                _cantidadDenominacionesRecibidas = _listaDenominaciones.ListadoLiquidaciones.Count();

                txtCajero.Text = _parte.IDCajero;
                txtNombre.Text = _parte.NombreCajero;
                txtParte.Text = _parte.NumeroParte.ToString();

                for(int i=0; i < _cantidadDenominacionesRecibidas && i < _cantidadMaximaDenominaciones; i++)
                {
                    ActualizarTextBoxDescripcion(i, _listaDenominaciones.ListadoLiquidaciones[i].Descripcion);
                    ActualizarTextBoxCantidad(i,true);
                    FormatearTextBoxDenominacion(i,false);
                }

                if(_usaBolsa)
                {
                    FormatearTextBoxBolsa(false);
                    FormatearTextBoxPrecinto(false);
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaLiquidacion:CargarDatosEnControles() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaLiquidacion:CargarDatosEnControles() Exception: {0}", ex.Message.ToString());
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
        private void ActualizarTextBoxDescripcion(int codigoDenominacion, string descripcion)
        {
            TextBox textbox;
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                textbox = (TextBox)FindName(string.Format("textDenominacion{0}", codigoDenominacion));
                if (!textbox.IsEnabled)
                    textbox.IsEnabled = true;
                textbox.Text = descripcion;
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
        private void ActualizarTextBoxCantidad(int codigoDenominacion, bool limpiar = false, int digito = -1)
        {
            TextBox textbox;
            string cantidadActual = string.Empty;
            string cantidadNueva = string.Empty;

            textbox = (TextBox)FindName(string.Format("txtCantidadDenominacion{0}", codigoDenominacion));

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
                        cantidadNueva = string.Format("{0}", cantidadActual.Substring(0, cantidadActual.Length-1));
                    }
                    else
                    {
                        cantidadNueva = "0";
                    }
                }
                else if (cantidadActual.Length < _maximaCantidadDigitosDenominacion)
                {
                    //Agrego digito
                    cantidadNueva = string.Format("{0}{1}", cantidadActual=="0"?"": cantidadActual, digito.ToString("D1"));
                }
            }

            if (cantidadActual != cantidadNueva)
            {
                Application.Current.Dispatcher.Invoke((Action)(() =>
                {
                    textbox.Text = cantidadNueva;
                }));
                ActualizarTextBoxImporte();
            }
        }

        private void ActualizarTextBoxObservacion( string caracter = "", bool limpiar = false )
        {
            string mensajeActual = string.Empty;
            string nuevoMensaje = string.Empty;

            mensajeActual = txtObservacion.Text;

            if( limpiar )
            {
                nuevoMensaje = string.Empty;
            }
            else
            {
                nuevoMensaje = mensajeActual;

                if( caracter == "" )
                {
                    if( mensajeActual.Length > 1 )
                    {
                        //Borro ultimo caracter ingresado
                        nuevoMensaje = string.Format( "{0}", mensajeActual.Substring( 0, mensajeActual.Length - 1 ) );
                    }
                    else
                    {
                        nuevoMensaje = string.Empty;
                    }
                }
                else if( mensajeActual.Length < _maximaCantidadCaracteresObs )
                {
                    //Agrego caracter
                    nuevoMensaje = string.Format( "{0}{1}", mensajeActual, caracter );
                }
            }

            if( mensajeActual != nuevoMensaje )
            {
                Application.Current.Dispatcher.BeginInvoke( (Action)( () =>
                {
                    txtObservacion.Text = nuevoMensaje;
                } ) );
            }
        }

        /// <summary>
        /// Actualiza textbox bolsa
        /// </summary>
        /// <param name="digito"></param>
        ///     Agrega digito (NO suma) a numero de bolsa
        ///     Si es valor por default elimina ultimo digito en textbox
        /// <param name="limpiar"></param>
        private void ActualizarTextBoxBolsa(string digito = "", bool limpiar = false)
        {
            string nroBolsaActual = string.Empty;
            string nroBolsaNuevo = string.Empty;

            nroBolsaActual = txtBolsa.Text;

            if (limpiar)
            {
                nroBolsaNuevo = string.Empty;
            }
            else
            {
                nroBolsaNuevo = nroBolsaActual;

                if (digito == "")
                {
                    if (nroBolsaActual.Length > 1)
                    {
                        //Borro ultimo digito ingresado
                        nroBolsaNuevo = string.Format("{0}", nroBolsaActual.Substring(0, nroBolsaActual.Length - 1));
                    }
                    else
                    {
                        nroBolsaNuevo = string.Empty;
                    }
                }
                else if (nroBolsaActual.Length < _maximaCantidadDigitosBolsa)
                {
                    //Agrego digito
                    nroBolsaNuevo = string.Format("{0}{1}", nroBolsaActual, digito);
                }
            }

            if (nroBolsaActual != nroBolsaNuevo)
            {
                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    txtBolsa.Text = nroBolsaNuevo;
                }));
            }
        }

        /// <summary>
        /// Actualiza textbox precinto
        /// </summary>
        /// <param name="digito"></param>
        ///     Agrega digito (NO suma) a numero de bolsa
        ///     Si es valor por default elimina ultimo digito en textbox
        /// <param name="limpiar"></param>
        private void ActualizarTextBoxPrecinto(string digito = "", bool limpiar = false)
        {
            string nroPrecintoActual = string.Empty;
            string nroPrecintoNuevo = string.Empty;

            nroPrecintoActual = txtPrecinto.Text;

            if (limpiar)
            {
                nroPrecintoNuevo = string.Empty;
            }
            else
            {
                nroPrecintoNuevo = nroPrecintoActual;

                if (digito == "")
                {
                    if (nroPrecintoActual.Length > 1)
                    {
                        //Borro ultimo digito ingresado
                        nroPrecintoNuevo = string.Format("{0}", nroPrecintoActual.Substring(0, nroPrecintoActual.Length - 1));
                    }
                    else
                    {
                        nroPrecintoNuevo = string.Empty;
                    }
                }
                else if (nroPrecintoActual.Length < _maximaCantidadDigitosBolsa)
                {
                    //Agrego digito
                    nroPrecintoNuevo = string.Format("{0}{1}", nroPrecintoActual, digito);
                }
            }

            if (nroPrecintoActual != nroPrecintoNuevo)
            {
                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    txtPrecinto.Text = nroPrecintoNuevo;
                }));
            }
        }

        private void LimpiarTextoTodosTextboxes()
        {
            TextBox textbox;

            for (int i = 0; i < 15; i++)
            {
                textbox = (TextBox)FindName(string.Format("textDenominacion{0}", i));
                textbox.Text = string.Empty;
                textbox = (TextBox)FindName(string.Format("txtCantidadDenominacion{0}", i));
                textbox.Text = string.Empty;
            }
        }


        /// <summary>
        /// Cambia formato textbox de denominacion (resaltar o formato normal)
        /// </summary>
        /// <param name="codigoDenominacion"></param>
        /// <param name="resaltar"></param>
        private void FormatearTextBoxDenominacion(int codigoDenominacion, bool resaltar = true)
        {
            TextBox textbox;
            textbox = (TextBox)FindName(string.Format("txtCantidadDenominacion{0}", codigoDenominacion));

            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (!textbox.IsEnabled)
                    textbox.IsEnabled = true;
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


        /// <summary>
        /// Cambia formato textbox de numero de bolsa (resaltar o formato normal)
        /// </summary>
        /// <param name="resaltar"></param>
        private void FormatearTextBoxBolsa(bool resaltar = true)
        {
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (_usaBolsa && !txtBolsa.IsEnabled)
                    txtBolsa.IsEnabled = true;
                if (resaltar)
                {
                    txtBolsa.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                }
                else
                {
                    txtBolsa.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                }
            }));
        }

        private void FormatearTextBoxObservacion( bool resaltar = true )
        {
            Application.Current.Dispatcher.BeginInvoke( (Action)( () =>
            {
                if( !txtObservacion.IsEnabled )
                    txtObservacion.IsEnabled = true;
                if( resaltar )
                {
                    txtObservacion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                }
                else
                {
                    txtObservacion.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                }
            } ) );
        }

        /// <summary>
        /// Cambia formato textbox de numero de precinto (resaltar o formato normal)
        /// </summary>
        /// <param name="resaltar"></param>
        private void FormatearTextBoxPrecinto(bool resaltar = true)
        {
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (_usaBolsa && !txtPrecinto.IsEnabled)
                    txtPrecinto.IsEnabled = true;

                if (resaltar)
                {
                    txtPrecinto.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                }
                else
                {
                    txtPrecinto.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                }
            }));
        }

        /// <summary>
        /// Obtiene importe presente en textbox de denominacion
        /// </summary>
        /// <param name="codigoDenominacion"></param>
        /// <returns></returns>
        private decimal ObtenerImporteDenominacion(int codigoDenominacion)
        {
            decimal monto = 0;

            TextBox textbox;
            textbox = (TextBox)FindName(string.Format("txtCantidadDenominacion{0}", codigoDenominacion));

            int cantidadDenominacion = 0;
            int.TryParse(textbox.Text, out cantidadDenominacion);

            monto = cantidadDenominacion * _listaDenominaciones.ListadoLiquidaciones[codigoDenominacion].Valor;

            return monto;
        }

        /// <summary>
        /// Actualiza el textbox de importe sumando todos los valores presentes en denominaciones
        /// </summary>
        private void ActualizarTextBoxImporte()
        {
            decimal monto = 0;

            for (int i = 0; i < _cantidadDenominacionesRecibidas && i < _cantidadMaximaDenominaciones; i++)
            {
                monto += ObtenerImporteDenominacion(i);
            }

            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                txtImporte.Text = Datos.FormatearMonedaAString(monto);
            }));
        }

        /// <summary>
        /// Metodo que mapea la informacion de cantidad ingresada por cada denominacion a una lista
        /// </summary>
        /// <returns></returns>
        private ListadoRetiroxDenominacion MapearCantidadesPorDenominacion()
        {
            ListadoRetiroxDenominacion listaLD = new ListadoRetiroxDenominacion();

            TextBox textbox;

            for (int i = 0; i < _cantidadDenominacionesRecibidas && i < _cantidadMaximaDenominaciones; i++)
            {
                textbox = (TextBox)FindName(string.Format("txtCantidadDenominacion{0}", i));
                int cantidadDenominacion = 0;
                if (int.TryParse(textbox.Text, out cantidadDenominacion))
                {
                    listaLD.ListadoLiquidaciones.Add(new RetiroxDenominacion(_listaDenominaciones.ListadoLiquidaciones[i], cantidadDenominacion));
                }
            }

            return listaLD;
        }
        #endregion

        private void ProcesarSimbolo(Opcion item)
        {
            _ventanaSimbolo.Close();
            if (txtObservacion.Text.Length < _maximaCantidadCaracteresObs)
            {
                string sNuevoSimb = item == null ? string.Empty : item?.Descripcion;
                ActualizarTextBoxObservacion(sNuevoSimb);
            }
        }

        #region Procesamiento de Pantalla tactil
        private void DENOM0_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(0);
            _pantalla.TecladoVisible();
        }
        private void DENOM1_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(1);
            _pantalla.TecladoVisible();
        }
        private void DENOM2_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(2);
            _pantalla.TecladoVisible();
        }
        private void DENOM3_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(3);
            _pantalla.TecladoVisible();
        }
        private void DENOM4_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(4);
            _pantalla.TecladoVisible();
        }
        private void DENOM5_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(5);
            _pantalla.TecladoVisible();
        }
        private void DENOM6_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(6);
            _pantalla.TecladoVisible();
        }
        private void DENOM7_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(7);
            _pantalla.TecladoVisible();
        }
        private void DENOM8_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(8);
            _pantalla.TecladoVisible();
        }
        private void DENOM9_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(9);
            _pantalla.TecladoVisible();
        }
        private void DENOM10_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(10);
            _pantalla.TecladoVisible();
        }
        private void DENOM11_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(11);
            _pantalla.TecladoVisible();
        }
        private void DENOM12_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(12);
            _pantalla.TecladoVisible();
        }
        private void DENOM13_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(13);
            _pantalla.TecladoVisible();
        }
        private void DENOM14_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxCantidad(14);
            _pantalla.TecladoVisible();
        }

        private void OBSER_Click(object sender, RoutedEventArgs e)
        {
            ActualizarTextBoxObservacion();
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

        #endregion

        private void txtCajero_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}
