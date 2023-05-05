using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Entidades;
using ModuloPantallaTeclado.Clases;
using System.Collections.Generic;
using Newtonsoft.Json;
using Entidades.Comunicacion;
using Entidades;
using Entidades.Logica;
using System.Windows.Input;
using System.Windows.Controls;
using Utiles.Utiles;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmMenuPagoDiferido { IngresoDocumento, ObtenerDatos, ConfirmoPago, ConfirmoImpresionTkt }

    /// <summary>
    /// Lógica de interacción para VentanaPagoDiferido.xaml
    /// </summary>
    public partial class VentanaPagoDiferido : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmMenuPagoDiferido _enmMenuPagoDiferido;
        private string _patenteIngresada;
        private string _documentoIngresado;
        private bool _solicitaDocumento = false;
        private int cantPagosDiferidos = 0;
        private int cantViolaciones = 0;
        private decimal totalPagosDiferidos = 0;
        private decimal totalViolaciones = 0;
        private decimal deudaTotal = 0;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgIngresoDocumento = "Ingrese el número de documento y presione {0} para confirmar, {1} para volver.";
        const string msgFirmaTicket = "Confirme la firma del ticket con {0}, {1} para salir.";
        const string msgConfirmePagoDif = "Confirme el pago diferido con {0}, {1} para volver.";
        const string msgConsultandoDatos = "Consultado datos...";
        const string msgClienteNoHallado = "Cliente no encontrado";
        #endregion

        #region Constructor de la clase
        /// <summary>
        /// Constructor de la clase
        /// </summary>
        /// <param name="padre"></param>
        public VentanaPagoDiferido(IPantalla padre)
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
            
            if(_pantalla.ParametroAuxiliar != string.Empty)
            {
                _solicitaDocumento = Utiles.ClassUtiles.ExtraerObjetoJson<Bolsa>(_pantalla.ParametroAuxiliar).UsaBolsa;

                if (!_solicitaDocumento)
                {
                    lblDocumento.Visibility = Visibility.Collapsed;
                    txtBoxDocumento.Visibility = Visibility.Collapsed;
                    _enmMenuPagoDiferido = enmMenuPagoDiferido.ConfirmoPago;
                    _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgConfirmePagoDif),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                    //Extraego los datos del total de deudas
                    DeudaPagoDiferido deudas = Utiles.ClassUtiles.ExtraerObjetoJson<DeudaPagoDiferido>(_pantalla.ParametroAuxiliar);
                    SetearDatosEnControles(deudas);
                }
                else
                {
                    _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgIngresoDocumento),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                    _enmMenuPagoDiferido = enmMenuPagoDiferido.IngresoDocumento;
                    txtBoxDocumento.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                }

                Vehiculo vehiculo = Utiles.ClassUtiles.ExtraerObjetoJson<Vehiculo>(_pantalla.ParametroAuxiliar);
                List<InfoDeuda> infoDeudas = Utiles.ClassUtiles.ExtraerObjetoJson<List<InfoDeuda>>(_pantalla.ParametroAuxiliar);

                if (infoDeudas != null)
                {
                    foreach (var tm in infoDeudas)
                    {
                        if (tm.Tipo == eTipoDeuda.PagoDiferido)
                        {
                            cantPagosDiferidos++;
                            totalPagosDiferidos += tm.Monto;
                        }
                        else if (tm.Tipo == eTipoDeuda.Violacion)
                        {
                            cantViolaciones++;
                            totalViolaciones = tm.Monto;
                        }
                    }
                    deudaTotal = totalViolaciones + totalPagosDiferidos;

                    if (cantPagosDiferidos > 0)
                    {
                        txtBoxPagosDif.Dispatcher.Invoke((Action)(() =>
                        {
                            txtBoxPagosDif.Text = cantPagosDiferidos.ToString();
                        }));

                        txtBoxPagosDifMonto.Dispatcher.Invoke((Action)(() =>
                        {
                            txtBoxPagosDifMonto.Text = "S/" + " " + totalPagosDiferidos.ToString("0.00");
                        }));
                    }
                    if (cantViolaciones > 0)
                    {
                        txtBoxViolaciones.Dispatcher.Invoke((Action)(() =>
                        {
                            txtBoxViolaciones.Text = cantViolaciones.ToString();
                        }));

                        txtBoxViolacionesMonto.Dispatcher.Invoke((Action)(() =>
                        {
                            txtBoxViolacionesMonto.Text = "S/"+ " " +totalViolaciones.ToString("0.00");
                        }));
                    }
                    txtBoxTotalDeuda.Dispatcher.Invoke((Action)(() =>
                    {
                        txtBoxTotalDeuda.Text = "S/" + " " + deudaTotal.ToString("0.00");
                    }));
                }
                if (vehiculo != null)
                {
                    _patenteIngresada = vehiculo.Patente;
                    txtBoxPatente.Dispatcher.Invoke((Action)(() =>
                    {
                        txtBoxPatente.Text = vehiculo.Patente;
                    }));
                }
            }
        }
        #endregion

        private void SetearDatosEnControles(DeudaPagoDiferido deudas)
        {
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                if (deudas != null)
                {
                    txtBoxViolaciones.Text = deudas.CantViolaciones.ToString();
                    txtBoxViolacionesMonto.Text = Datos.FormatearMonedaAString(deudas.MontoTotalViolaciones);
                    txtBoxPagosDif.Text = deudas.CantPagosDiferidos.ToString();
                    txtBoxPagosDifMonto.Text = Datos.FormatearMonedaAString(deudas.MontoTotalPagosDiferidos);
                    txtBoxTotalDeuda.Text = Datos.FormatearMonedaAString(deudas.MontoTotalViolaciones + deudas.MontoTotalPagosDiferidos);
                }
                else
                {
                    txtBoxViolaciones.Text = string.Empty;
                    txtBoxViolacionesMonto.Text = string.Empty;
                    txtBoxPagosDif.Text = string.Empty;
                    txtBoxPagosDifMonto.Text = string.Empty;
                    txtBoxTotalDeuda.Text = string.Empty;
                }
            }));
        }

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
            FrameworkElement control = (FrameworkElement)borderPagoDiferido.Child;
            borderPagoDiferido.Child = null;
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
                else if (comandoJson.Accion == enmAccion.PAGO_DIF && comandoJson.Operacion != string.Empty)
                {
                    //Logica me envia los datos de deuda
                    if(_enmMenuPagoDiferido == enmMenuPagoDiferido.ObtenerDatos)
                    {
                        DeudaPagoDiferido deudas = Utiles.ClassUtiles.ExtraerObjetoJson<DeudaPagoDiferido>(_pantalla.ParametroAuxiliar);
                        if (deudas != null)
                        {
                            SetearDatosEnControles(deudas);
                            _enmMenuPagoDiferido = enmMenuPagoDiferido.ConfirmoPago;
                            _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgConfirmePagoDif),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                        }
                        else
                        {
                            _enmMenuPagoDiferido = enmMenuPagoDiferido.IngresoDocumento;
                            txtBoxDocumento.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                            _pantalla.MensajeDescripcion(msgClienteNoHallado);
                        }
                    }
                }
                else
                    bRet = true;
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaPagoDiferido:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar enviar datos a logica.");
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
                _logger.Debug("VentanaPagoDiferido:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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
                if (_enmMenuPagoDiferido == enmMenuPagoDiferido.IngresoDocumento || (_enmMenuPagoDiferido == enmMenuPagoDiferido.ConfirmoPago && !_solicitaDocumento))
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        EnviarDatosALogica(enmStatus.Abortada, enmAccion.PAGO_DIF, string.Empty);
                    }));
                }
                else if(_enmMenuPagoDiferido == enmMenuPagoDiferido.ConfirmoPago && _solicitaDocumento)
                {
                    _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgIngresoDocumento),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                    _enmMenuPagoDiferido = enmMenuPagoDiferido.IngresoDocumento;
                    txtBoxDocumento.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                    SetearDatosEnControles(null);
                }
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                if (_enmMenuPagoDiferido == enmMenuPagoDiferido.IngresoDocumento)
                {
                    _documentoIngresado = txtBoxDocumento.Text;
                    txtBoxDocumento.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                    _pantalla.MensajeDescripcion(msgConsultandoDatos);
                    _enmMenuPagoDiferido = enmMenuPagoDiferido.ObtenerDatos;
                    //Solicito a logica datos del documento
                    InfoCliente cliente = new InfoCliente();
                    cliente.Ruc = _documentoIngresado;
                    Utiles.ClassUtiles.InsertarDatoVia(cliente, ref listaDV);
                    EnviarDatosALogica(enmStatus.Ok, enmAccion.PAGO_DIF, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                }
                else if(_enmMenuPagoDiferido == enmMenuPagoDiferido.ConfirmoPago)
                {
                    _pantalla.MensajeDescripcion(
                                string.Format(Traduccion.Traducir(msgFirmaTicket),
                                Teclado.GetEtiquetaTecla("Enter"), Teclado.GetEtiquetaTecla("Escape")),
                                false
                                );
                    _enmMenuPagoDiferido = enmMenuPagoDiferido.ConfirmoImpresionTkt;
                }
                else if(_enmMenuPagoDiferido == enmMenuPagoDiferido.ConfirmoImpresionTkt)
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        //Envio a logica la solicitud del pago diferido
                        Vehiculo vehiculo = new Vehiculo();
                        vehiculo.Patente = _patenteIngresada;
                        Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);
                        EnviarDatosALogica(enmStatus.Ok, enmAccion.CREAR_PAGO_DIF, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    }));
                }
            }
            else if (Teclado.IsBackspaceKey(tecla))
            {
                if (_enmMenuPagoDiferido == enmMenuPagoDiferido.IngresoDocumento)
                {
                    if (txtBoxDocumento.Text.Length > 0)
                        txtBoxDocumento.Text = txtBoxPatente.Text.Remove(txtBoxDocumento.Text.Length - 1);
                }
            }
            else
            {
                if (_enmMenuPagoDiferido == enmMenuPagoDiferido.IngresoDocumento)
                {
                    if (Teclado.IsLowerCaseOrNumberKey(tecla))
                        txtBoxDocumento.Text += Teclado.GetKeyNumericValue(tecla);
                }
            }
        }
        #endregion

        #region Pantalla Tactil

        private void ENTER_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(Key.Enter);
        }
        private void ESC_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(Key.Escape);
        }
        #endregion
    }
}
