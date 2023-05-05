using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Clases;
using Newtonsoft.Json;
using Entidades.Comunicacion;
using Entidades;
using Entidades.Logica;
using System.Collections.Generic;
using Entidades.ComunicacionBaseDatos;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Controls;
using Utiles.Utiles;
using System.Diagnostics;
using System.IO;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmMenuRetiro { IngresoImporte, IngresoBolsa, IngresoPrecinto, Confirmacion }

    /// <summary>
    /// Lógica de interacción para VentanaRetiroAnticipado.xaml
    /// </summary>
    public partial class VentanaRetiroAnticipado : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmMenuRetiro _enmMenuRetiro;
        private decimal _importeARetirar = 0;
        private int _numeroDeBolsa = 0;
        private int _numeroDePrecinto = 0;
        private int _caracteresSimboloMoneda;
        private Moneda _moneda = null;
        private bool _usaBolsa = false;
        private Parte _parte = null;
        private Brush _brushFondoRegular = Brushes.Black;
        private Brush _brushLetraRegular = Brushes.White;
        private Brush _brushFondoResaltado = Brushes.Blue;
        private Brush _brushLetraResaltado = Brushes.Yellow;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgIngresoMonto = "Ingrese el importe a retirar y confirme";
        const string msgIngresoBolsa = "Ingrese el número de bolsa y confirme";
        const string msgIngresoPrecinto = "Ingrese el número de precinto y confirme";
        const string msgConfirmeDatos = "Confirme los datos ingresados";
        #endregion

        #region Constructor de la clase
        public VentanaRetiroAnticipado(IPantalla padre)
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
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                _pantalla.MensajeDescripcion(msgIngresoMonto);
                _enmMenuRetiro = enmMenuRetiro.IngresoImporte;
                txtImporte.Text = "S/ ";
                _caracteresSimboloMoneda = txtImporte.Text.Length;

                txtImporte.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyleHighlighted );

                if (_pantalla.ParametroAuxiliar != string.Empty)
                {
                    //_moneda = Utiles.ClassUtiles.ExtraerObjetoJson<RetiroAnticipado>(_pantalla.ParametroAuxiliar).Moneda;
                    _moneda = Utiles.ClassUtiles.ExtraerObjetoJson<Moneda>( _pantalla.ParametroAuxiliar );
                    _usaBolsa = Utiles.ClassUtiles.ExtraerObjetoJson<Bolsa>(_pantalla.ParametroAuxiliar).UsaBolsa;
                    if(_usaBolsa)
                    {
                        txtBolsa.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyle );
                        txtPrecinto.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyle );
                        //FormatearTextBoxBolsa();
                        //FormatearTextBoxPrecinto();
                    }
                    else
                    {
                        lblNumeroBolsa.Visibility = Visibility.Collapsed;
                        txtBolsa.Visibility = Visibility.Collapsed;
                        lblNumeroPrecinto.Visibility = Visibility.Collapsed;
                        txtPrecinto.Visibility = Visibility.Collapsed;
                    }
                    CargarDatosEnControles(_pantalla.ParametroAuxiliar);
                }
            }));
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
            FrameworkElement control = (FrameworkElement)borderVentanaRetiro.Child;
            borderVentanaRetiro.Child = null;
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
                    Causa causa = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(comandoJson.Operacion);

                    if (causa.Codigo == eCausas.AperturaTurno
                        || causa.Codigo == eCausas.CausaCierre)
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
                bRet = false;
                _logger.Debug("VentanaRetiroAnticipado:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
                _logger.Debug("VentanaRetiroAnticipado:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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
                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    txtCajero.Text = _parte.IDCajero;
                    txtNombre.Text = _parte.NombreCajero;
                    txtParte.Text = _parte.NumeroParte.ToString();
                }));
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaRetiroAnticipado:CargarDatosEnControles() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("CargarDatosEnControles:CargarDatosEnControles() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
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
                if (_enmMenuRetiro == enmMenuRetiro.IngresoImporte)
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        txtImporte.Text = string.Empty;
                        txtBolsa.Text = string.Empty;
                        EnviarDatosALogica(enmStatus.Abortada, enmAccion.RETIRO_ANT, string.Empty);
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);

                        txtImporte.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyleHighlighted );
                        _pantalla.TecladoOculto();
                    } ));
                }
                else if (_enmMenuRetiro == enmMenuRetiro.IngresoBolsa && _usaBolsa)
                {
                    SetTextoBotonesAceptarCancelar("Enter", "Escape");
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(msgIngresoMonto);
                        _enmMenuRetiro = enmMenuRetiro.IngresoImporte;
                        txtBolsa.Text = string.Empty;
                        txtBolsa.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyle );
                        txtImporte.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyleHighlighted );
                    } ));
                }
                else if (_enmMenuRetiro == enmMenuRetiro.IngresoPrecinto && _usaBolsa)
                {
                    SetTextoBotonesAceptarCancelar("Enter", "Escape");
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(msgIngresoBolsa);
                        _enmMenuRetiro = enmMenuRetiro.IngresoBolsa;
                        txtPrecinto.Text = string.Empty;
                        txtBolsa.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyleHighlighted );
                        txtPrecinto.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyle );
                    } ));
                }
                else if (_enmMenuRetiro == enmMenuRetiro.Confirmacion)
                {
                    if (_usaBolsa)
                    {
                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            _pantalla.MensajeDescripcion(msgIngresoPrecinto);
                            SetTextoBotonesAceptarCancelar("Enter", "Escape");
                            _enmMenuRetiro = enmMenuRetiro.IngresoPrecinto;

                            txtBolsa.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyleHighlighted );
                        } ));
                    }
                    else
                    {
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            _pantalla.MensajeDescripcion(msgIngresoMonto);
                            _enmMenuRetiro = enmMenuRetiro.IngresoImporte;
                            txtImporte.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyleHighlighted );
                        } ));
                    }
                }
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                if (_enmMenuRetiro == enmMenuRetiro.IngresoImporte)
                {
                    if (txtImporte.Text.Length > _caracteresSimboloMoneda - 1)
                    {
                        decimal importe = 0;
                        if (decimal.TryParse(txtImporte.Text.Remove(0, 3).Replace('.',','), out importe))
                        {
                            _importeARetirar = importe;
                        }
                        else
                        {
                            _importeARetirar = 0;
                            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                            {
                                txtImporte.Text = "S/ ";
                            }));
                        }
                    }

                    if (_usaBolsa)
                    {
                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            if (txtImporte.Text.Length > _caracteresSimboloMoneda && _importeARetirar > 0)
                            {
                                _pantalla.MensajeDescripcion(msgIngresoBolsa);
                                SetTextoBotonesAceptarCancelar("Enter", "Escape");
                                _enmMenuRetiro = enmMenuRetiro.IngresoBolsa;
                                txtBolsa.Text = string.Empty;

                                txtImporte.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyle );
                                txtBolsa.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyleHighlighted );
                            }
                            else
                                txtImporte.Text = "S/";
                        }));
                    }
                    else
                    {
                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            if (txtImporte.Text.Length > _caracteresSimboloMoneda && _importeARetirar > 0)
                            {
                                _pantalla.MensajeDescripcion(msgConfirmeDatos);
                                SetTextoBotonesAceptarCancelar("Enter", "Escape");
                                _enmMenuRetiro = enmMenuRetiro.Confirmacion;

                                txtImporte.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyle );
                            }
                            else
                                txtImporte.Text = "S/ ";
                        }));
                    }
                }
                else if (_enmMenuRetiro == enmMenuRetiro.IngresoBolsa && _usaBolsa)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(msgIngresoPrecinto);
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        _enmMenuRetiro = enmMenuRetiro.IngresoPrecinto;
                        if (txtBolsa.Text.Length > 0)
                            _numeroDeBolsa = int.Parse(txtBolsa.Text);
                        else
                            _numeroDeBolsa = 0;

                        txtBolsa.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyle );
                        txtPrecinto.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyleHighlighted );
                    } ));
                }
                else if (_enmMenuRetiro == enmMenuRetiro.IngresoPrecinto && _usaBolsa)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(msgConfirmeDatos);
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        _enmMenuRetiro = enmMenuRetiro.Confirmacion;
                        if (txtPrecinto.Text.Length > 0)
                            _numeroDePrecinto = int.Parse(txtPrecinto.Text);
                        else
                            _numeroDePrecinto = 0;

                        txtPrecinto.Style = Estilo.FindResource<Style>( ResourceList.TextBoxStyle );
                    } ));
                }
                else if( _enmMenuRetiro == enmMenuRetiro.Confirmacion )
                {
                    RetiroAnticipado retiro = new RetiroAnticipado( _importeARetirar, _numeroDeBolsa, _numeroDePrecinto );
                    //retiro.Moneda = _moneda;
                    retiro.PorDenominacion = false;
                    Utiles.ClassUtiles.InsertarDatoVia( retiro, ref listaDV );
                    Utiles.ClassUtiles.InsertarDatoVia( _moneda, ref listaDV );
                    Application.Current.Dispatcher.BeginInvoke( (Action)( () =>
                    {
                        EnviarDatosALogica( enmStatus.Ok, enmAccion.RETIRO_ANT, JsonConvert.SerializeObject( listaDV, jsonSerializerSettings ) );
                        _pantalla.MensajeDescripcion( string.Empty );
                        _pantalla.TecladoOculto();
                    } ) );
                }
            }
            else if (Teclado.IsBackspaceKey(tecla))
            {
                if (_enmMenuRetiro == enmMenuRetiro.IngresoImporte)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        if (txtImporte.Text.Length > _caracteresSimboloMoneda)
                            txtImporte.Text = txtImporte.Text.Remove(txtImporte.Text.Length - 1);
                    }));
                }
                else if (_enmMenuRetiro == enmMenuRetiro.IngresoBolsa && _usaBolsa)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        if (txtBolsa.Text.Length > 0)
                            txtBolsa.Text = txtBolsa.Text.Remove(txtBolsa.Text.Length - 1);
                    }));
                }
                else if (_enmMenuRetiro == enmMenuRetiro.IngresoPrecinto && _usaBolsa)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        if (txtPrecinto.Text.Length > 0)
                            txtPrecinto.Text = txtPrecinto.Text.Remove(txtPrecinto.Text.Length - 1);
                    }));
                }
            }
            else
            {
                if (_enmMenuRetiro == enmMenuRetiro.IngresoImporte)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        if ((Teclado.IsNumericKey(tecla) || Teclado.IsDecimalKey(tecla)) && txtImporte.Text.Length < (_caracteresSimboloMoneda + 8))
                        {
                            if (Teclado.IsDecimalKey(tecla) && !txtImporte.Text.Contains("."))
                                txtImporte.Text += ".";
                            else
                            {
                                int digito = Teclado.GetKeyNumericValue(tecla);
                                int cantCifrasDecimales;
                                if (txtImporte.Text.Contains("."))
                                    cantCifrasDecimales = txtImporte.Text.Length - txtImporte.Text.IndexOf(".");
                                else
                                    cantCifrasDecimales = 0;

                                if (digito > -1 && cantCifrasDecimales <= Datos.GetCantidadDecimales())
                                    txtImporte.Text += digito;
                            }
                        }
                    }));
                }
                else if (_enmMenuRetiro == enmMenuRetiro.IngresoBolsa && _usaBolsa)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        if( Teclado.IsNumericKey( tecla ) )
                            txtBolsa.Text += Teclado.GetKeyNumericValue(tecla);
                    }));
                }
                else if (_enmMenuRetiro == enmMenuRetiro.IngresoPrecinto && _usaBolsa)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        if( Teclado.IsNumericKey( tecla ) )
                            txtPrecinto.Text += Teclado.GetKeyNumericValue(tecla);
                    }));
                }
            }
        }
        #endregion

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

        private void IMPORTE_Click(object sender, RoutedEventArgs e)
        {
            _enmMenuRetiro = enmMenuRetiro.IngresoImporte;
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
    }
}
