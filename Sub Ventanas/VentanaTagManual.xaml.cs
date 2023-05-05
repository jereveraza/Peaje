using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Clases;
using Newtonsoft.Json;
using Entidades.Comunicacion;
using Entidades;
using Entidades.Logica;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Controls;
using Utiles.Utiles;
using Entidades.ComunicacionBaseDatos;
using System.Diagnostics;
using System.IO;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmMenuTagManual { IngresoPatente, ConsultaDatos, MuestroDatos }

    /// <summary>
    /// Lógica de interacción para VentanaTagManual.xaml
    /// </summary>
    public partial class VentanaTagManual : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmMenuTagManual _enmMenuTagManual;
        private string _patenteIngresada;
        private InfoTag _tagVehiculo;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgSeleccionOpciones = "Ingrese la patente y confirme";
        const string msgFormatoPatenteErr = "Formato de patente incorrecto";
        const string msgConfirmaTagManual = "Confirme los datos";
        #endregion

        #region Constructor de la clase
        public VentanaTagManual(IPantalla padre)
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
                _pantalla.MensajeDescripcion(msgSeleccionOpciones);
                _enmMenuTagManual = enmMenuTagManual.MuestroDatos;
                //Si me llego la patente de logica, la muestro el textbox
                if (_pantalla.ParametroAuxiliar != string.Empty)
                {
                    CargarDatosEnControles(_pantalla.ParametroAuxiliar);
                }
                _pantalla.TecladoVisible();
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
            FrameworkElement control = (FrameworkElement)borderVentanaTagManual.Child;
            borderVentanaTagManual.Child = null;
            Close();
            return control;
        }
        #endregion

        #region Metodo de carga de textboxes de datos
        private void CargarDatosEnControles(string datos)
        {
            try
            {                
                _tagVehiculo = Utiles.ClassUtiles.ExtraerObjetoJson<InfoTag>(datos);
                txtBoxPatente.Text = _tagVehiculo.Patente;
                txtBoxNumeroTag.Text = _tagVehiculo.NumeroTag.Replace(" ", "");
                txtBoxMarca.Text = _tagVehiculo.Marca;
                txtBoxModelo.Text = _tagVehiculo.Modelo;
                txtBoxColor.Text = _tagVehiculo.Color;
                txtBoxNombre.Text = _tagVehiculo.NombreCuenta;
                txtBoxCategoria.Text = _tagVehiculo.CategoDescripcionLarga;
                _pantalla.MensajeDescripcion(msgConfirmaTagManual);
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
                if (_enmMenuTagManual == enmMenuTagManual.ConsultaDatos)
                {
                    //Logica encontro la patente y devuelve los datos
                    if (comandoJson.Accion == enmAccion.TAGMANUAL && comandoJson.Operacion != string.Empty)
                    {
                        Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            _enmMenuTagManual = enmMenuTagManual.MuestroDatos;
                            CargarDatosEnControles(comandoJson.Operacion);
                        }));
                    }
                    //Logica no pudo encontrar el vehiculo, cierro la ventana
                    else if (comandoJson.Accion == enmAccion.TAGMANUAL && comandoJson.Operacion == string.Empty)
                    {
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                }
                else if (comandoJson.CodigoStatus == enmStatus.Ok
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
            catch (Exception ex)
            {
                _logger.Debug("VentanaTagManual:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
                _logger.Debug("VentanaTagManual:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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
            if (_enmMenuTagManual == enmMenuTagManual.IngresoPatente)
            {
                //Espero el ingreso de patente del usuario
                if (Teclado.IsConfirmationKey(tecla))
                {
                    //Compruebo si el formato de la patente es correcto
                    _patenteIngresada = txtBoxPatente.Text;
                    if (Clases.Utiles.EsPatenteValida(Clases.Utiles.FormatoPatente.Abierto, _patenteIngresada, true))
                    {
                        //Envio la consulta a logica y seteo el nuevo estado
                        Application.Current.Dispatcher.Invoke((Action)(() =>
                        {
                            _enmMenuTagManual = enmMenuTagManual.ConsultaDatos;

                            Vehiculo vehiculo = new Vehiculo();
                            vehiculo.Patente = _patenteIngresada;
                            Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);

                            TagBD tagBD = Utiles.ClassUtiles.ExtraerObjetoJson<TagBD>( _pantalla.ParametroAuxiliar );

                            if( tagBD != null )
                                Utiles.ClassUtiles.InsertarDatoVia( tagBD, ref listaDV );

                            EnviarDatosALogica(enmStatus.Ok, enmAccion.TAG_PATENTE, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                            _pantalla.TecladoOculto();
                        }));
                    }
                    else
                    {
                        //Si se ingreso la patente incorrecta, aviso al usuario
                        _pantalla.MensajeDescripcion(msgFormatoPatenteErr);
                        _patenteIngresada = string.Empty;
                    }
                }
                else if (Teclado.IsEscapeKey(tecla))
                {
                    txtBoxPatente.Text = string.Empty;
                    _tagVehiculo = null;
                    EnviarDatosALogica(enmStatus.Abortada, enmAccion.T_TAGMANUAL, string.Empty );
                    _pantalla.MensajeDescripcion(string.Empty);
                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    _pantalla.TecladoOculto();
                    _pantalla.CargarSubVentana(enmSubVentana.FormaPago);
                }
                else if (Teclado.IsBackspaceKey(tecla))
                {
                    if (txtBoxPatente.Text.Length > 0)
                        txtBoxPatente.Text = txtBoxPatente.Text.Remove(txtBoxPatente.Text.Length - 1);
                }
                else
                {
                    if (Teclado.IsLowerCaseOrNumberKey(tecla))
                        txtBoxPatente.Text += Teclado.GetKeyAlphaNumericValue(tecla);
                }
            }
            else if (_enmMenuTagManual == enmMenuTagManual.ConsultaDatos)
            {
                if (Teclado.IsEscapeKey(tecla))
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        txtBoxPatente.Text = string.Empty;
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        _pantalla.TecladoOculto();
                        _pantalla.CargarSubVentana(enmSubVentana.FormaPago);
                    }));
                }
            }
            else if (_enmMenuTagManual == enmMenuTagManual.MuestroDatos)
            {
                if (Teclado.IsEscapeKey(tecla))
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        EnviarDatosALogica(enmStatus.Abortada, enmAccion.T_TAGMANUAL, string.Empty);
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        _pantalla.TecladoOculto();
                        _pantalla.CargarSubVentana(enmSubVentana.FormaPago);
                    }));
                }
                else if (Teclado.IsConfirmationKey(tecla))
                {
                    //Envio el tag a logica
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        var tag = JsonConvert.SerializeObject(_tagVehiculo);
                        Vehiculo vehiculo = new Vehiculo();
                        vehiculo.InfoTag = _tagVehiculo;
                        Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);

                        TagBD tagBD = Utiles.ClassUtiles.ExtraerObjetoJson<TagBD>( _pantalla.ParametroAuxiliar );

                        if( tagBD != null)
                            Utiles.ClassUtiles.InsertarDatoVia( tagBD, ref listaDV );


                        EnviarDatosALogica(enmStatus.Ok, enmAccion.TAG, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        _pantalla.MensajeDescripcion(string.Empty);
                        _pantalla.CargarSubVentana(enmSubVentana.Principal);
                        _pantalla.TecladoOculto();
                    }));
                }
            }
        }
        #endregion

        private void PLACA_Click(object sender, RoutedEventArgs e)
        {
            _enmMenuTagManual = enmMenuTagManual.IngresoPatente;
            _pantalla.TecladoVisible();
        }

        private void ENTER_Click(object sender, RoutedEventArgs e)
        {
            _pantalla.CargarSubVentana(enmSubVentana.Principal);
            ProcesarTecla(Key.Enter);
        }

        private void TARJETA_Click(object sender, RoutedEventArgs e)
        {
            //_pantalla.CargarSubVentana(enmSubVentana.Principal);

            //Envio el tag a logica
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                List<DatoVia> listaDV = new List<DatoVia>();

                var tag = JsonConvert.SerializeObject(_tagVehiculo);
                Vehiculo vehiculo = new Vehiculo();
                vehiculo.InfoTag = _tagVehiculo;
                Utiles.ClassUtiles.InsertarDatoVia(vehiculo, ref listaDV);

                TagBD tagBD = Utiles.ClassUtiles.ExtraerObjetoJson<TagBD>(_pantalla.ParametroAuxiliar);

                if (tagBD != null)
                    Utiles.ClassUtiles.InsertarDatoVia(tagBD, ref listaDV);


                EnviarDatosALogica(enmStatus.Ok, enmAccion.TAG_TARJ, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                _pantalla.MensajeDescripcion(string.Empty);
                //_pantalla.CargarSubVentana(enmSubVentana.Principal);
                //_pantalla.TecladoOculto();
            }));
        }

        private void ESC_Click(object sender, RoutedEventArgs e)
        {
            ProcesarTecla(System.Windows.Input.Key.Escape);
        }
    }
}
