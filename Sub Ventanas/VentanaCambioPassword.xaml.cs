using System;
using System.Windows;
using ModuloPantallaTeclado.Interfaces;
using ModuloPantallaTeclado.Clases;
using System.Collections.Specialized;
using System.Configuration;
using Newtonsoft.Json;
using Entidades.Comunicacion;
using Entidades;
using System.Collections.Generic;
using System.Windows.Input;
using Utiles.Utiles;
using System.Windows.Controls;

namespace ModuloPantallaTeclado.Sub_Ventanas
{
    public enum enmCambioPassword { IngresoPassword, ReIngresoPassword, Confirmar }

    /// <summary>
    /// Lógica de interacción para VentanaCambioPassword.xaml
    /// </summary>
    public partial class VentanaCambioPassword : Window, ISubVentana
    {
        #region Variables y propiedades de clase
        private IPantalla _pantalla = null;
        private enmCambioPassword _enmCambioPassword;
        private string _password;
        private int _maximoCaracteres;
        private NameValueCollection _caracteres;
        private Login _login;
        private Causa _causa;
        private NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
        #endregion

        #region Mensajes de descripcion
        const string msgIngrese1pass = "Ingrese la contraseña nueva";
        const string msgIngrese2pass = "Reingrese la contraseña";
        const string msgErrorPassword = "Error: ambas contraseñas deben coincidir";
        const string msgErrorNuevoPasswordIgualActual = "Error: la nueva contraseña debe ser distinta a la actual";
        const string msgConfirmeCambio = "Confirme el cambio de contraseña";
        #endregion

        #region Constructor de la clase
        public VentanaCambioPassword(IPantalla padre)
        {
            InitializeComponent();
            _pantalla = padre;
        }
        #endregion

        #region Evento de carga de la ventana
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            _caracteres = (NameValueCollection)ConfigurationManager.GetSection("caracteres");
            _maximoCaracteres = Convert.ToInt32(_caracteres["maximoCaracteres"]);
            _enmCambioPassword = enmCambioPassword.IngresoPassword;

            txtIngresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyleHighlighted);
            txtReingresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyle);

            SetTextoBotonesAceptarCancelar("Enter", "Escape", true);
            Clases.Utiles.TraducirControles<TextBlock>(gridIngresoSistema);
            _pantalla.TecladoVisible();

            try
            {
                _login = Utiles.ClassUtiles.ExtraerObjetoJson<Login>(_pantalla.ParametroAuxiliar);                
                _causa = Utiles.ClassUtiles.ExtraerObjetoJson<Causa>(_pantalla.ParametroAuxiliar);
            }
            catch (JsonException jsonEx)
            {
                _logger.Debug("CambioPassword:Grid_Loaded() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("CambioPassword:Grid_Loaded() Exception: {0}", ex.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                _pantalla.MensajeDescripcion(msgIngrese1pass);
                lblCambioPassword.Text = _causa.Descripcion;
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
            FrameworkElement control = (FrameworkElement)borderCambioPassword.Child;
            borderCambioPassword.Child = null;
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
            catch (JsonException jsonEx)
            {
                _logger.Debug("VentanaCambioPassword:RecibirDatosLogica() JsonException: {0}", jsonEx.Message.ToString());
                _logger.Warn("Error al intentar deserializar una Respuesta de logica.");
            }
            catch (Exception ex)
            {
                _logger.Debug("VentanaCambioPassword:RecibirDatosLogica() Exception: {0}", ex.Message.ToString());
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
                _pantalla.EnviarDatosALogica(status,Accion, Operacion);
            }
            catch (Exception ex)
            {
                _logger.Debug("CambioPassword:EnviarDatosALogica() Exception: {0}", ex.Message.ToString());
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
            if (Teclado.IsBackspaceKey(tecla))
            {
                if (_enmCambioPassword == enmCambioPassword.ReIngresoPassword)
                {
                    if (txtReingresoPassword.Password.Length > 0)
                        txtReingresoPassword.Password = txtReingresoPassword.Password.Remove(txtReingresoPassword.Password.Length - 1);
                }
                else if (_enmCambioPassword == enmCambioPassword.IngresoPassword)
                {
                    if (txtIngresoPassword.Password.Length > 0)
                        txtIngresoPassword.Password = txtIngresoPassword.Password.Remove(txtIngresoPassword.Password.Length - 1);
                }
            }
            else if (Teclado.IsConfirmationKey(tecla))
            {
                //GAB: Comento porque se decidió sacar confirmación adicional (si el operador escribe la misma clave dos veces se asume que quiere esa)
                /*if (_enmCambioPassword == enmCambioPassword.Confirmar)
                {
                    if (txtIngresoPassword.Password == txtReingresoPassword.Password)
                    {                        
                        _password = txtIngresoPassword.Password;
                        _login.Password = Utiles.ClassUtiles.GetHashString(_login.Usuario + _password);
                        Utiles.ClassUtiles.InsertarDatoVia(_login, ref listaDV);
                        var nuevoPass = JsonConvert.SerializeObject(listaDV, jsonSerializerSettings);
                        EnviarDatosALogica(enmStatus.Ok,enmAccion.CAMBIO_PASS, nuevoPass);
                        txtIngresoPassword.Password = string.Empty;
                        txtReingresoPassword.Password = string.Empty;
                        _pantalla.MensajeDescripcion(string.Empty);
                        //_pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                    else
                    {
                        txtIngresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyleHighlighted);
                        txtReingresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyle);                        
                        _pantalla.MensajeDescripcion(msgErrorPassword);
                        txtIngresoPassword.Password = string.Empty;
                        txtReingresoPassword.Password = string.Empty;
                        _enmCambioPassword = enmCambioPassword.IngresoPassword;
                    }
                }*/
                if (txtIngresoPassword.Password != string.Empty && _enmCambioPassword == enmCambioPassword.IngresoPassword)
                {
                    _password = txtIngresoPassword.Password.ToUpper();
                    _login.Password = Utiles.ClassUtiles.GetHashString(_login.Usuario.ToUpper() + _password);
                    if (_login.Password == _login.PasswordViejo)
                    {
                        _logger.Debug("Password nueva igual a password vieja");
                        _password = string.Empty;
                        _login.Password = string.Empty;
                        txtIngresoPassword.Password = string.Empty;
                        txtIngresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyleHighlighted);
                        txtReingresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyle);
                        _pantalla.MensajeDescripcion(msgErrorNuevoPasswordIgualActual);
                    }
                    else
                    {
                        txtIngresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyle);
                        txtReingresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyleHighlighted);
                        _enmCambioPassword = enmCambioPassword.ReIngresoPassword;
                        SetTextoBotonesAceptarCancelar("Enter", "Escape");
                        _pantalla.MensajeDescripcion(msgIngrese2pass);
                    }
                }
                else if (txtReingresoPassword.Password != string.Empty && _enmCambioPassword == enmCambioPassword.ReIngresoPassword)
                {
                    //_enmCambioPassword = enmCambioPassword.Confirmar;
                    //_pantalla.MensajeDescripcion(msgConfirmeCambio);
                    if (txtIngresoPassword.Password == txtReingresoPassword.Password)
                    {
                        _password = txtIngresoPassword.Password.ToUpper();
                        _login.Password = Utiles.ClassUtiles.GetHashString(_login.Usuario.ToUpper() + _password);
                        Utiles.ClassUtiles.InsertarDatoVia(_login, ref listaDV);
                        var nuevoPass = JsonConvert.SerializeObject(listaDV, jsonSerializerSettings);
                        EnviarDatosALogica(enmStatus.Ok, enmAccion.CAMBIO_PASS, nuevoPass);
                        txtIngresoPassword.Password = string.Empty;
                        txtReingresoPassword.Password = string.Empty;
                        _pantalla.MensajeDescripcion(string.Empty);
                        //_pantalla.CargarSubVentana(enmSubVentana.Principal);
                    }
                    else
                    {
                        txtIngresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyleHighlighted);
                        txtReingresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyle);
                        _pantalla.MensajeDescripcion(msgErrorPassword);
                        txtIngresoPassword.Password = string.Empty;
                        txtReingresoPassword.Password = string.Empty;
                        _enmCambioPassword = enmCambioPassword.IngresoPassword;
                    }
                }
            }
            else if (Teclado.IsEscapeKey(tecla))
            {
                if (_enmCambioPassword == enmCambioPassword.Confirmar)
                {
                    txtIngresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyle);
                    txtReingresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyleHighlighted);
                    _enmCambioPassword = enmCambioPassword.ReIngresoPassword;
                    SetTextoBotonesAceptarCancelar("Enter", "Escape", true);
                    txtReingresoPassword.Password = string.Empty;
                    _pantalla.MensajeDescripcion(msgIngrese2pass);
                }

                else if (_enmCambioPassword == enmCambioPassword.ReIngresoPassword)
                {
                    txtIngresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyleHighlighted);
                    txtReingresoPassword.Style = Estilo.FindResource<Style>(ResourceList.PasswordStyle);
                    _enmCambioPassword = enmCambioPassword.IngresoPassword;
                    txtReingresoPassword.Password = string.Empty;
                    txtIngresoPassword.Password = string.Empty;
                    _pantalla.MensajeDescripcion(msgIngrese1pass);
                }

                else if (_enmCambioPassword == enmCambioPassword.IngresoPassword)
                {

                    txtIngresoPassword.Password = string.Empty;
                    txtReingresoPassword.Password = string.Empty;
                    _pantalla.MensajeDescripcion(string.Empty);
                    _pantalla.CargarSubVentana(enmSubVentana.Principal);
                    EnviarDatosALogica(enmStatus.Abortada, enmAccion.CAMBIO_PASS, string.Empty);
                }
            }
            else
            {
                if (_enmCambioPassword == enmCambioPassword.IngresoPassword)
                {
                    if (Teclado.IsLowerCaseOrNumberKey(tecla))
                    {
                        if (txtIngresoPassword.Password.Length < _maximoCaracteres)
                            txtIngresoPassword.Password += Teclado.GetKeyAlphaNumericValue(tecla);
                    }
                }
                else if (_enmCambioPassword == enmCambioPassword.ReIngresoPassword)
                {
                    if (Teclado.IsLowerCaseOrNumberKey(tecla))
                    {
                        if (txtReingresoPassword.Password.Length < _maximoCaracteres)
                            txtReingresoPassword.Password += Teclado.GetKeyAlphaNumericValue(tecla);
                    }
                }
            }
        }
        #endregion

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
