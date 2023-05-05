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
using Entidades.ComunicacionBaseDatos;
using ModuloPantallaTeclado.Clases;
using System.Windows.Media;
using System.Windows.Input;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmMenuRetiroPorDenominacion { IngresoCantidades, IngresoBolsa, IngresoPrecinto, Confirmacion }

    /// <summary>
    /// Lógica de interacción para VentanaRetiroAnticipadoDenominaciones.xaml
    /// </summary>
    public partial class VentanaRetiroAnticipadoDenominaciones : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmMenuRetiroPorDenominacion _enmMenuRetiro = enmMenuRetiroPorDenominacion.IngresoCantidades;
        private long _importeARetirar = 0;
        private int _numeroDeBolsa = 0;
        private int _numeroDePrecinto = 0;
        private bool _usaBolsa = false;
        private Moneda _moneda = null;
        private int _cantidadMaximaDenominaciones = 10;
        private int _maximaCantidadDigitosDenominacion = 4;
        private int _maximaCantidadDigitosBolsa = 8;
        private int _cantidadDenominacionesRecibidas = 0;
        private int _denominacionActual = 0;
        private Parte _parte;
        private Brush _brushFondoRegular = Brushes.Black;
        private Brush _brushLetraRegular = Brushes.White;
        private Brush _brushFondoResaltado = Brushes.Blue;
        private Brush _brushLetraResaltado = Brushes.Yellow;
        private ListadoDenominaciones _listaDenominaciones = null;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgIngresoCantidad = "Ingrese cantidad de denominacion y confirme con {0}, {1} para volver.";
        const string msgIngresoBolsa = "Ingrese el número de bolsa y confirme con {0}, {1} para volver.";
        const string msgIngresoPrecinto = "Ingrese el número de precinto y confirme con {0}, {1} para volver.";
        const string msgConfirmeDatos = "Confirme los datos ingresados con {0}, {1} para volver.";
        #endregion

        #region Constructor de la clase
        public VentanaRetiroAnticipadoDenominaciones(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgIngresoCantidad),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
            Clases.Utiles.TraducirControles<TextBlock>(Grid_Principal);
            if (_pantalla.ParametroAuxiliar != string.Empty)
            {
                _moneda = Utiles.ClassUtiles.ExtraerObjetoJson<RetiroAnticipado>(_pantalla.ParametroAuxiliar).Moneda;
                _usaBolsa = Utiles.ClassUtiles.ExtraerObjetoJson<Bolsa>(_pantalla.ParametroAuxiliar).UsaBolsa;
                CargarDatosEnControles(_pantalla.ParametroAuxiliar);
                FormatearTextBoxDenominacion(_denominacionActual);
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
            FrameworkElement control = (FrameworkElement)borderVentanaRetiroPorDenominacion.Child;
            borderVentanaRetiroPorDenominacion.Child = null;
            Close();
            return control;
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
                if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.IngresoCantidades)
                {
                    if(_denominacionActual == 0)
                    {
                        EnviarDatosALogica(enmStatus.Abortada, enmAccion.LIQUIDACION, string.Empty);
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                    else
                    {
                        FormatearTextBoxDenominacion(_denominacionActual,false);
                        ActualizarTextBoxCantidad(_denominacionActual--, true);
                        FormatearTextBoxDenominacion(_denominacionActual);
                    }
                }
                else if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.IngresoBolsa && _usaBolsa)
                {
                    _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgIngresoCantidad),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                    _enmMenuRetiro = enmMenuRetiroPorDenominacion.IngresoCantidades;
                    ActualizarTextBoxBolsa("",true);
                    FormatearTextBoxDenominacion(_denominacionActual);
                    FormatearTextBoxBolsa(false);
                }
                else if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.IngresoPrecinto && _usaBolsa)
                {
                    _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgIngresoBolsa),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                    _enmMenuRetiro = enmMenuRetiroPorDenominacion.IngresoBolsa;
                    ActualizarTextBoxPrecinto("", true);
                    FormatearTextBoxPrecinto(false);
                    FormatearTextBoxBolsa();
                }
                else if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.Confirmacion)
                {
                    if (_usaBolsa)
                    {
                        _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgIngresoPrecinto),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                        _enmMenuRetiro = enmMenuRetiroPorDenominacion.IngresoPrecinto;
                        FormatearTextBoxPrecinto();
                    }
                    else
                    {
                        _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgIngresoCantidad),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                        _enmMenuRetiro = enmMenuRetiroPorDenominacion.IngresoCantidades;                        
                        FormatearTextBoxDenominacion(_denominacionActual);
                    }
                }
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.IngresoCantidades)
                {
                    if ( _denominacionActual >= _cantidadMaximaDenominaciones-1
                      || _denominacionActual >= _cantidadDenominacionesRecibidas-1)
                    {
                        if (_usaBolsa)
                        {
                            _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgIngresoBolsa),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                            _enmMenuRetiro = enmMenuRetiroPorDenominacion.IngresoBolsa;
                            FormatearTextBoxDenominacion(_denominacionActual, false);
                            FormatearTextBoxBolsa();
                        }
                        else
                        {
                            FormatearTextBoxPrecinto(false);
                            _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgConfirmeDatos),
                                Teclado.GetEtiquetaTecla("Cash"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                            _enmMenuRetiro = enmMenuRetiroPorDenominacion.Confirmacion;
                        }
                    }
                    else
                    {
                        FormatearTextBoxDenominacion(_denominacionActual, false);
                        _denominacionActual++;
                        FormatearTextBoxDenominacion(_denominacionActual);
                    }
                }
                else if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.IngresoBolsa && _usaBolsa)
                {
                    FormatearTextBoxBolsa(false);
                    _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgIngresoPrecinto),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                    _enmMenuRetiro = enmMenuRetiroPorDenominacion.IngresoPrecinto;
                    FormatearTextBoxPrecinto();
                }
                else if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.IngresoPrecinto && _usaBolsa)
                {
                    FormatearTextBoxPrecinto(false);
                    _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgConfirmeDatos),
                                Teclado.GetEtiquetaTecla("Cash"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                    _enmMenuRetiro = enmMenuRetiroPorDenominacion.Confirmacion;
                }
            }
            else if (Teclado.IsBackspaceKey(tecla))
            {
                if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.IngresoCantidades)
                {
                    ActualizarTextBoxCantidad(_denominacionActual);
                }
                else if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.IngresoBolsa && _usaBolsa)
                {
                    ActualizarTextBoxBolsa();
                }
                else if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.IngresoPrecinto && _usaBolsa)
                {
                    ActualizarTextBoxPrecinto();
                }
            }
            else if (Teclado.IsCashKey(tecla))
            {
                if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.Confirmacion)
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

                    if (txtImporte.Text.Length > 0)
                        _importeARetirar = int.Parse(txtImporte.Text);
                    else
                        _importeARetirar = 0;

                    RetiroAnticipado retiro = new RetiroAnticipado(_importeARetirar * 1000, _numeroDeBolsa, _numeroDePrecinto);
                    retiro.Moneda = _moneda;
                    retiro.PorDenominacion = false;

                    retiro.ListaLiquidacionxDenominacion = MapearCantidadesPorDenominacion();

                    Utiles.ClassUtiles.InsertarDatoVia(retiro, ref listaDV);

                    EnviarDatosALogica(enmStatus.Ok, enmAccion.RETIRO_ANT, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    _pantalla.MensajeDescripcion(string.Empty);
                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                }
            }
            else
            {
                if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.IngresoCantidades)
                {
                    if( Teclado.IsNumericKey(tecla) )
                    {
                        ActualizarTextBoxCantidad(_denominacionActual, false, Teclado.GetKeyNumericValue(tecla));
                    }
                }
                else if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.IngresoBolsa && _usaBolsa)
                {
                    if (Teclado.IsNumericKey(tecla))
                    {
                        ActualizarTextBoxBolsa(Teclado.GetKeyNumericValue(tecla).ToString());
                    }
                }
                else if (_enmMenuRetiro == enmMenuRetiroPorDenominacion.IngresoPrecinto && _usaBolsa)
                {
                    if (Teclado.IsNumericKey(tecla))
                    {
                        ActualizarTextBoxPrecinto(Teclado.GetKeyNumericValue(tecla).ToString());
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
                else
                    bRet = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaRetiroAnticipadoDenominaciones:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
                _logger.Debug("VentanaRetiroAnticipadoDenominaciones:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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

    /*
    Denominacion den0 = new Denominacion();
    den0.Valor = 1;
    den0.CodigoDenominacion = 0;
    den0.Descripcion = "1";
    _listaDenominaciones.ListadoLiquidaciones.Add(den0);

    Denominacion den1 = new Denominacion();
    den1.Valor = 10;
    den1.CodigoDenominacion = 1;
    den1.Descripcion = "10";
    _listaDenominaciones.ListadoLiquidaciones.Add(den1);

    Denominacion den2 = new Denominacion();
    den2.Valor = 100;
    den2.CodigoDenominacion = 2;
    den2.Descripcion = "100";
    _listaDenominaciones.ListadoLiquidaciones.Add(den2);

    _usaBolsa = true;
    */


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
                _logger.Debug("VentanaRetiroAnticipadoDenominaciones:CargarDatosEnControles() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaRetiroAnticipadoDenominaciones:CargarDatosEnControles() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
        }
        #endregion

        #region Metodos para acceder a textboxs
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
                    textbox.Background = _brushFondoResaltado;
                    textbox.Foreground = _brushLetraResaltado;
                }
                else
                {
                    textbox.Background = _brushFondoRegular;
                    textbox.Foreground = _brushLetraRegular;
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
                    txtBolsa.Background = _brushFondoResaltado;
                    txtBolsa.Foreground = _brushLetraResaltado;
                }
                else
                {
                    txtBolsa.Background = _brushFondoRegular;
                    txtBolsa.Foreground = _brushLetraRegular;
                }
            }));
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
                    txtPrecinto.Background = _brushFondoResaltado;
                    txtPrecinto.Foreground = _brushLetraResaltado;
                }
                else
                {
                    txtPrecinto.Background = _brushFondoRegular;
                    txtPrecinto.Foreground = _brushLetraRegular;
                }
            }));
        }

        /// <summary>
        /// Obtiene importe presente en textbox de denominacion
        /// </summary>
        /// <param name="codigoDenominacion"></param>
        /// <returns></returns>
        private int ObtenerImporteDenominacion(int codigoDenominacion)
        {
            int monto = 0;

            TextBox textbox;
            textbox = (TextBox)FindName(string.Format("txtCantidadDenominacion{0}", codigoDenominacion));            

            int cantidadDenominacion = 0;
            int.TryParse(textbox.Text, out cantidadDenominacion);

            monto = cantidadDenominacion * Convert.ToInt32(_listaDenominaciones.ListadoLiquidaciones[codigoDenominacion].Valor);

            return monto;
        }

        /// <summary>
        /// Actualiza el textbox de importe sumando todos los valores presentes en denominaciones
        /// </summary>
        private void ActualizarTextBoxImporte()
        {
            int monto = 0;

            for (int i = 0; i < _cantidadDenominacionesRecibidas && i < _cantidadMaximaDenominaciones; i++)
            {
                monto += ObtenerImporteDenominacion(i);
            }

            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                txtImporte.Text = monto.ToString();
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
    }
}
