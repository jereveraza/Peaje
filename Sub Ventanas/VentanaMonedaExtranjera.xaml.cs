using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Entidades;
using ModuloPantallaTeclado.Clases;
using Newtonsoft.Json;
using System.Collections.Generic;
using Entidades.Comunicacion;
using Entidades;
using System.Windows.Input;
using System.Windows.Controls;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmMonedaExtranjera { SeleccionMoneda, IngresoMonto, Confirmacion}

    /// <summary>
    /// Lógica de interacción para VentanaMonedaExtranjera.xaml
    /// </summary>
    public partial class VentanaMonedaExtranjera : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private List<Moneda> _listaMonedas = null;
        private enmMonedaExtranjera _enmMonedaExtranjera;
        private int _itemSeleccionado,_cantMonedasDisponibles;
        private ulong _tarifaMonedaLocal;
        private int _caracteresSimboloMoneda;
        private ulong _cotizacion;
        private ulong _pagoIngresado;

        const long REDONDEARFRACCION = 1000 / 2 - 1;
        const long REDONDEAR = 1000;

        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgSeleccionMoneda = "Seleccione una moneda y presione {0} para confirmar, {1} para volver.";
        const string msgIngresoMonto = "Ingrese el pago en moneda extranjera y presione {0}, {1} para volver.";
        const string msgConfirmeMonto = "Confirme el monto con {0}, {1} para volver.";
        #endregion

        #region Constructor de la clase
        /// <summary>
        /// Constructor de la clase
        /// </summary>
        /// <param name="padre"></param>
        public VentanaMonedaExtranjera(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
            _listaMonedas = new List<Moneda>();
            //Obtengo la tarifa local y la muestro en el textbox correspondiente
            _tarifaMonedaLocal = Convert.ToUInt64(_pantalla.VehiculoRecibido.Tarifa);
        }
        #endregion

        #region Metodo que actualiza el valor de los textboxes
        private void ActualizarTextBoxes()
        {
            //Cargo una tarifa en moneda extranjera
            _cotizacion = (dataGridMonedas.SelectedItem as Moneda).Cotizacion;
            txtBoxTarifaExtra.Text = Datos.FormatearMonedaAString((_tarifaMonedaLocal/ _cotizacion));
            if (_enmMonedaExtranjera == enmMonedaExtranjera.IngresoMonto)
            {
                if (_pagoIngresado != 0)
                    txtBoxVuelto.Text = Datos.FormatearMonedaAString(CalculaDiferenciaVuelto(_pagoIngresado, _tarifaMonedaLocal, _cotizacion));
                else
                    txtBoxVuelto.Text = string.Empty;
                if (_enmMonedaExtranjera == enmMonedaExtranjera.SeleccionMoneda)
                    txtBoxPago.Text = string.Empty;
            }
        }
        #endregion

        #region Metodo de carga del datagrid de monedas
        private void CargarListaMonedas()
        {
            //Cargo las opciones del listbox
            if (_pantalla.ParametroAuxiliar != string.Empty)
            {
                try
                {                    
                    _listaMonedas = Utiles.ClassUtiles.ExtraerObjetoJson<ListaMoneda>(_pantalla.ParametroAuxiliar).ListaMonedas;

                    _listaMonedas.Sort((x, y) => x.Orden.CompareTo(y.Orden));
                    _cantMonedasDisponibles = _listaMonedas.Count;
                    //Cargo el combobox
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        dataGridMonedas.Items.Clear();
                        dataGridMonedas.ItemsSource = _listaMonedas;
                        dataGridMonedas.Items.Refresh();
                        dataGridMonedas.SelectedIndex = 0;
                        ActualizarTextBoxes();
                        _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgSeleccionMoneda),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                    }));
                }
                catch (JsonException jsonEx)
                {
                    _logger.Debug("VentanaMonedaExtranjera:CargarListaMonedas() JsonException: {0}", jsonEx.Message.ToString());
                    _logger.Warn("Error al intentar deserializar una lista de monedas desde de logica.");
                }
                catch (Exception ex)
                {
                    _logger.Debug("VentanaMonedaExtranjera:CargarListaMonedas() Exception: {0}", ex.Message.ToString());
                    _logger.Warn("Error al intentar deserializar una lista de monedas desde de logica.");
                }
            }
        }
        #endregion

        #region Metodo de calculo de la diferencia del vuelto
        private long CalculaDiferenciaVuelto(ulong Pago, ulong Tarifa, ulong Cotizacion)
        {
            bool bNegativo = false;
            long tmpcuenta,Diferencia;
            //Calcula el vuelto a entregar
            //El vuelto se redondea a Pesos completos mas cercanos
            //TODO Ver la cotizaron por cuanto estaba multiplicada
            tmpcuenta = (long)(Pago * Cotizacion - Tarifa);
            Diferencia = tmpcuenta;
            //Redondeamos
            if (Diferencia < 0)
            {
                bNegativo = true;
                Diferencia = -Diferencia;
            }
            Diferencia = (Diferencia + REDONDEARFRACCION) / REDONDEAR * REDONDEAR;
            if (bNegativo)
                Diferencia = -Diferencia;
            return Diferencia;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            Clases.Utiles.TraducirControles<TextBlock>(Grid_Principal);
            _enmMonedaExtranjera = enmMonedaExtranjera.SeleccionMoneda;
            _itemSeleccionado = 1;
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                _caracteresSimboloMoneda = Datos.GetSimboloMonedaReferencia().Length;
                CargarListaMonedas();
            }));
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
            FrameworkElement control = (FrameworkElement)borderMonedaExtranjera.Child;
            borderMonedaExtranjera.Child = null;
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
                _logger.Debug("VentanaMonedaExtranjera:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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
                if (_enmMonedaExtranjera == enmMonedaExtranjera.SeleccionMoneda)
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        var pagoExtranjero = new Moneda();
                        Utiles.ClassUtiles.InsertarDatoVia(pagoExtranjero, ref listaDV);
                        EnviarDatosALogica(enmStatus.Abortada, enmAccion.PAGO_EXTRAN, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    }));
                }
                else if (_enmMonedaExtranjera == enmMonedaExtranjera.IngresoMonto)
                {
                    _enmMonedaExtranjera = enmMonedaExtranjera.SeleccionMoneda;
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        txtBoxPago.Text = string.Empty;
                        txtBoxVuelto.Text = string.Empty;
                        _pantalla.MensajeDescripcion(
                                 string.Format(Traduccion.Traducir(msgSeleccionMoneda),
                                 Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                 false
                                 );
                    }));
                }
                else if(_enmMonedaExtranjera == enmMonedaExtranjera.Confirmacion)
                {
                    _enmMonedaExtranjera = enmMonedaExtranjera.IngresoMonto;
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(
                                 string.Format(Traduccion.Traducir(msgIngresoMonto),
                                 Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                 false
                                 );
                    }));
                }
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                if (_enmMonedaExtranjera == enmMonedaExtranjera.SeleccionMoneda)
                {
                    _enmMonedaExtranjera = enmMonedaExtranjera.IngresoMonto;
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(
                                 string.Format(Traduccion.Traducir(msgIngresoMonto),
                                 Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                 false
                                 );
                        txtBoxPago.Text = Datos.GetSimboloMonedaReferencia();
                        txtBoxVuelto.Text = string.Empty;
                    }));
                }
                else if (_enmMonedaExtranjera == enmMonedaExtranjera.IngresoMonto)
                {
                    _enmMonedaExtranjera = enmMonedaExtranjera.Confirmacion;
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(
                                 string.Format(Traduccion.Traducir(msgConfirmeMonto),
                                 Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                 false
                                 );
                    }));
                }
                else if(_enmMonedaExtranjera == enmMonedaExtranjera.Confirmacion)
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        var pagoExtranjero = _listaMonedas[_itemSeleccionado - 1];
                        Utiles.ClassUtiles.InsertarDatoVia(pagoExtranjero, ref listaDV);
                        EnviarDatosALogica(enmStatus.Ok, enmAccion.PAGO_EXTRAN, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    }));
                }
            }
            else if (Teclado.IsUpKey(tecla))
            {
                if (_enmMonedaExtranjera == enmMonedaExtranjera.SeleccionMoneda)
                {
                    if (_itemSeleccionado > 1)
                    {
                        dataGridMonedas.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            if (_itemSeleccionado > 1 && _itemSeleccionado <= _cantMonedasDisponibles)
                            {
                                _itemSeleccionado--;
                                dataGridMonedas.SelectedIndex = _itemSeleccionado - 1;
                                ActualizarTextBoxes();
                            }
                        }));
                    }
                }
            }
            else if (Teclado.IsDownKey(tecla))
            {
                if (_enmMonedaExtranjera == enmMonedaExtranjera.SeleccionMoneda)
                {
                    if (_itemSeleccionado < _cantMonedasDisponibles)
                    {
                        dataGridMonedas.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            _itemSeleccionado++;
                            dataGridMonedas.SelectedIndex = _itemSeleccionado - 1;
                            ActualizarTextBoxes();
                        }));
                    }
                }
            }
            else if (Teclado.IsBackspaceKey(tecla))
            {
                if (_enmMonedaExtranjera == enmMonedaExtranjera.IngresoMonto)
                {
                    if (txtBoxPago.Text.Length > _caracteresSimboloMoneda)
                    {
                        txtBoxPago.Text = txtBoxPago.Text.Remove(txtBoxPago.Text.Length - 1);
                        string _sPagoIngresado = txtBoxPago.Text.Replace(Datos.GetSimboloMonedaReferencia(), "");
                        if (_sPagoIngresado != "")
                            _pagoIngresado = ulong.Parse(_sPagoIngresado) * 1000;
                        else
                            _pagoIngresado = 0;
                        ActualizarTextBoxes();
                    }
                }
            }
            else if (Teclado.IsNumericKey(tecla))
            {
                if (_enmMonedaExtranjera == enmMonedaExtranjera.SeleccionMoneda)
                {
                    int teclaNumer = (int)Teclado.GetKeyNumericValue(tecla);
                    if (teclaNumer > 0 && teclaNumer <= _cantMonedasDisponibles)
                    {
                        _itemSeleccionado = teclaNumer;
                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            dataGridMonedas.SelectedIndex = _itemSeleccionado - 1;
                            ActualizarTextBoxes();
                        }));
                    }
                }
                else if (_enmMonedaExtranjera == enmMonedaExtranjera.IngresoMonto)
                {
                    txtBoxPago.Text += Teclado.GetKeyNumericValue(tecla);
                    string _sPagoIngresado = txtBoxPago.Text.Replace(Datos.GetSimboloMonedaReferencia(), "");
                    if (_sPagoIngresado != "")
                        _pagoIngresado = ulong.Parse(_sPagoIngresado) * 1000;
                    else
                        _pagoIngresado = 0;
                    ActualizarTextBoxes();
                }
            }
        }
        #endregion
    }
}
