using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Clases;
using System.Collections.Specialized;
using System.Configuration;
using System.Windows.Media;
using Newtonsoft.Json;
using Entidades.Comunicacion;
using Entidades;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Controls;
using Utiles.Utiles;
using System.Diagnostics;
using System.IO;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmIngresoUsuario { Usuario, Password, Confirmar }

    /// <summary>
    /// Lógica de interacción para IngresoSistema.xaml
    /// </summary>
    public partial class IngresoSistema : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private int _maximoCaracteres;
        private enmIngresoUsuario _enmIngresoUsuario;
        private IPantalla _pantalla = null;
        private NameValueCollection _sectorTeclas;
        private Causa _causaRecibida;
        private bool _confirmarLogin = true;
        public string ParametroAuxiliar { set; get; }
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgCodigoCajero = "Ingrese código de cajero";
        const string msgPasswordCajero = "Ingrese la contraseña";
        const string msgConfirmaOperacion = "Confirme la operación";
        const string msgDeseaConfirmar = "Está seguro que desea confirmar?";
        #endregion

        #region Constructor de la clase
        /// <summary>
        /// Constructor de la clase
        /// </summary>
        /// <param name="padre"></param>
        public IngresoSistema(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        public void SetTextoBotonesAceptarCancelar(string TeclaConfirmacion, string TeclaCancelacion, bool BtnAceptarSiguiente = false)
        {
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                btnAceptar.Content = Traduccion.Traducir(BtnAceptarSiguiente  ? "Siguiente" : "Confirmar") + " [" + Teclado.GetEtiquetaTecla(TeclaConfirmacion) + "]";
                btnCancelar.Content = Traduccion.Traducir("Volver") + " [" + Teclado.GetEtiquetaTecla(TeclaCancelacion) + "]";
            }));
        }

        #region Inicializacion de la ventana
        private void gridIngresoSistema_Loaded(object sender, RoutedEventArgs e)
        {
            _sectorTeclas = (NameValueCollection)ConfigurationManager.GetSection("caracteres");
            _maximoCaracteres = Convert.ToInt32(_sectorTeclas["maximoCaracteres"]);
            _enmIngresoUsuario = enmIngresoUsuario.Usuario;

            SetTextoBotonesAceptarCancelar("Enter", "Escape", true);
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                txtCodigoCajero.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                txtPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyle);
                txtPassword.VerticalContentAlignment = VerticalAlignment.Center;
                txtPassword.MaxLength = _maximoCaracteres;
            }));
            
            _pantalla.MensajeDescripcion(msgCodigoCajero);
            _pantalla.TecladoVisible();

            try
            {
                _causaRecibida = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(_pantalla.ParametroAuxiliar);
                Opcion opcionConfirmacion = Utiles.ClassUtiles.ExtraerObjetoJson<Opcion>(_pantalla.ParametroAuxiliar);

                _confirmarLogin = opcionConfirmacion.Confirmar;

                lblIngresoSistema.Dispatcher.Invoke((Action)(() => lblIngresoSistema.Text = _causaRecibida.Descripcion));
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("IngresoSistema:gridIngresoSistema_Loaded() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("IngresoSistema:gridIngresoSistema_Loaded() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            Clases.Utiles.TraducirControles<TextBlock>(gridIngresoSistema);
        }
        #endregion

        #region Metodo para obtener el control desde el padre
        /// <summary>
        /// Obtiene el control que se quiere agregar a la ventana principal
        /// <returns>FrameworkElement</returns>
        /// </summary>
        public FrameworkElement ObtenerControlPrincipal()
        {
            FrameworkElement control = (FrameworkElement)borderIngresoSistema.Child;
            borderIngresoSistema.Child = null;
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
                else if (comandoJson.CodigoStatus == enmStatus.Error)
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                    {
                        txtPassword.Password = string.Empty;
                        txtPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyleHighlighted);
                    }));
                    _enmIngresoUsuario = enmIngresoUsuario.Password;
                }
                else
                    bRet = true;
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("IngresoSistema:RecibirDatosLogica() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("IngresoSistema:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
                _logger.Debug("IngresoSistema:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar enviar datos a logica.");
            }
        }
        #endregion

        #region Metodo de procesamiento de tecla recibida
        public void ProcesarTeclaUp(Key tecla)
        {
            if (Teclado.IsBackspaceKey(tecla))
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
            if (Teclado.IsBackspaceKey(tecla))
            {
                if (_enmIngresoUsuario == enmIngresoUsuario.Usuario)
                {
                    if (txtCodigoCajero.Text.Length > 0)
                        txtCodigoCajero.Text = txtCodigoCajero.Text.Remove(txtCodigoCajero.Text.Length - 1);
                }

                else if (_enmIngresoUsuario == enmIngresoUsuario.Password)
                {
                    if (txtPassword.Password.Length > 0)
                        txtPassword.Password = txtPassword.Password.Remove(txtPassword.Password.Length - 1);
                }
            }
            else  if (Teclado.IsConfirmationKey(tecla))
            {
                if (_enmIngresoUsuario == enmIngresoUsuario.Confirmar)
                {
                    _enmIngresoUsuario = enmIngresoUsuario.Usuario;
                    string idPassHash = string.Empty;
                    
                    idPassHash = Utiles.ClassUtiles.GetHashString(txtCodigoCajero.Text.ToUpper() + txtPassword.Password.ToUpper());

                    var login = new Login(txtCodigoCajero.Text, idPassHash);

                    SetTextoBotonesAceptarCancelar("Enter", "Escape");

                    //Si la contraseña esta vacia hay que avisarle a lógica
                    if (string.IsNullOrEmpty(txtPassword.Password))
                        login.PasswordVacia = true;
                    else
                        login.PasswordVacia = false;

                    Utiles.ClassUtiles.InsertarDatoVia(login, ref listaDV);
                    Utiles.ClassUtiles.InsertarDatoVia(_causaRecibida, ref listaDV);

                    EnviarDatosALogica(enmStatus.Ok, enmAccion.LOGIN, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    /*txtCodigoCajero.Text = string.Empty;
                    txtPassword.Password = string.Empty;
                    txtCodigoCajero.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                    txtPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyle);*/
                    _pantalla.MensajeDescripcion(string.Empty);
                    //_pantalla.CargarSubVentana(enmSubVentana.Principal);
                }

                if (_enmIngresoUsuario == enmIngresoUsuario.Usuario)
                {
                    if (txtCodigoCajero.Text != string.Empty)
                    {
                        _enmIngresoUsuario = enmIngresoUsuario.Password;
                        SetTextoBotonesAceptarCancelar("Enter", "Escape", true);
                        Application.Current.Dispatcher.Invoke((Action)(() =>
                        {
                            txtCodigoCajero.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                            txtPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyleHighlighted);
                        }));
                        _pantalla.MensajeDescripcion(msgPasswordCajero);
                    }
                }

                else if (_enmIngresoUsuario == enmIngresoUsuario.Password)
                {
                    if(_confirmarLogin)
                    {
                        _enmIngresoUsuario = enmIngresoUsuario.Confirmar;
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        _pantalla.MensajeDescripcion(msgConfirmaOperacion);
                        _pantalla.TecladoOculto();
                    }
                    else
                    {
                        _enmIngresoUsuario = enmIngresoUsuario.Usuario;
                        string idPassHash = string.Empty;

                        idPassHash = Utiles.ClassUtiles.GetHashString(txtCodigoCajero.Text.ToUpper() + txtPassword.Password.ToUpper());

                        var login = new Login(txtCodigoCajero.Text, idPassHash);

                        //Si la contraseña esta vacia hay que avisarle a lógica
                        if (string.IsNullOrEmpty(txtPassword.Password))
                            login.PasswordVacia = true;
                        else
                            login.PasswordVacia = false;
                        Application.Current.Dispatcher.Invoke((Action)(() =>
                        {
                            txtCodigoCajero.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                            txtPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyle);
                        }));

                        Utiles.ClassUtiles.InsertarDatoVia(login, ref listaDV);
                        Utiles.ClassUtiles.InsertarDatoVia(_causaRecibida, ref listaDV);

                        EnviarDatosALogica(enmStatus.Ok, enmAccion.LOGIN, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                        /*txtCodigoCajero.Text = string.Empty;
                        txtPassword.Password = string.Empty;*/
                        _pantalla.MensajeDescripcion(string.Empty);
                        //_pantalla.CargarSubVentana(enmSubVentana.Principal);
                        _pantalla.CargarSubVentana(enmSubVentana.Categorias);
                        _pantalla.TecladoOculto();


                    }
                }
            }
            else if (Teclado.IsEscapeKey(tecla))
            {
                if (_enmIngresoUsuario == enmIngresoUsuario.Confirmar)
                {
                    _enmIngresoUsuario = enmIngresoUsuario.Password;
                    SetTextoBotonesAceptarCancelar("Enter", "Escape", true);
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        txtCodigoCajero.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                        txtPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyleHighlighted);
                    }));
                    _pantalla.MensajeDescripcion(msgPasswordCajero);

                }

                else if (_enmIngresoUsuario == enmIngresoUsuario.Password)
                {
                    txtPassword.Password = string.Empty;
                    _enmIngresoUsuario = enmIngresoUsuario.Usuario;
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        txtCodigoCajero.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                        txtPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyle);
                    }));
                    _pantalla.MensajeDescripcion(msgCodigoCajero);
                }
                else if (_enmIngresoUsuario == enmIngresoUsuario.Usuario)
                {
                    Application.Current.Dispatcher.Invoke((Action)(() =>
                    {
                        txtCodigoCajero.Text = string.Empty;
                        txtPassword.Password = string.Empty;
                        txtCodigoCajero.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                        txtPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyle);
                    }));
                    var login = new Login(string.Empty, string.Empty);
                    Utiles.ClassUtiles.InsertarDatoVia(login, ref listaDV);
                    EnviarDatosALogica(enmStatus.Abortada, enmAccion.LOGIN, JsonConvert.SerializeObject(listaDV, jsonSerializerSettings));
                    _pantalla.MensajeDescripcion(string.Empty);
                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    

                }
            }
            else
            {
                if (_enmIngresoUsuario == enmIngresoUsuario.Usuario)
                {
                    if (txtCodigoCajero.Text.Length < _maximoCaracteres)
                    {
                        if (Teclado.IsLowerCaseOrNumberKey(tecla))
                        {
                            txtCodigoCajero.Text += Teclado.GetKeyAlphaNumericValue(tecla);
                        }
                    }
                    else
                    {
                        txtCodigoCajero.Dispatcher.Invoke((Action)(() =>
                        {
                            txtCodigoCajero.Background = new SolidColorBrush(Color.FromRgb(255, 0, 0));
                        }));
                    }
                }
                else if (_enmIngresoUsuario == enmIngresoUsuario.Password)
                {
                    if (txtPassword.Password.Length < _maximoCaracteres)
                    {
                        if (Teclado.IsLowerCaseOrNumberKey(tecla))
                            txtPassword.Password += Teclado.GetKeyAlphaNumericValue(tecla);
                    }
                    else
                    {

                    }
                }
            }
        }
        #endregion

        #region Procesamiento de Pantalla Tactil

        private void CODIGO_Click(object sender, RoutedEventArgs e)
        {
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

        private void CODIGO_Click(object sender, MouseButtonEventArgs e)
        {
            _enmIngresoUsuario = enmIngresoUsuario.Usuario;
            SetTextoBotonesAceptarCancelar("Enter", "Escape", true);
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                txtCodigoCajero.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyleHighlighted);
                txtPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyle);
            }));
            _pantalla.MensajeDescripcion(msgPasswordCajero);
            _pantalla.TecladoVisible();
        }

        private void PASSWORD_Click(object sender, MouseButtonEventArgs e)
        {
            _enmIngresoUsuario = enmIngresoUsuario.Password;
            SetTextoBotonesAceptarCancelar("Enter", "Escape", true);
            Application.Current.Dispatcher.Invoke((Action)(() =>
            {
                txtCodigoCajero.Style = Estilo.FindResource<Style>(ResourceList.TextBoxStyle);
                txtPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyleHighlighted);
            }));
            _pantalla.MensajeDescripcion(msgPasswordCajero);
            _pantalla.TecladoVisible();
        }
    }
}
