using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace ModuloPantallaTeclado.Clases
{
    public class Teclado
    {
        #region Variables y Propiedades de clase
        private static Dictionary<string, ConfigTecla> _sectorTeclas = new Dictionary<string, ConfigTecla>();
        private static Key _TeclaEnie = Key.F8;
        private static NLog.Logger _logger = NLog.LogManager.GetLogger("logfile");
        private JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        private class ConfigTecla
        {
            public string Etiqueta { set; get; }
            public string Tecla { set; get; }
            public string Modificador { set; get; }

            public ConfigTecla()
            {
                Etiqueta = string.Empty;
                Tecla = string.Empty;
                Modificador = string.Empty;
            }
        }
        #endregion

        #region Metodo de carga de las teclas en memoria desde app.config
        public static void CargarTeclasFuncion(ref Dictionary<string, string> sectorTeclas)
        {
            foreach (KeyValuePair<string, string> entry in sectorTeclas)
            {
                try
                {
                    ConfigTecla Tecla = JsonConvert.DeserializeObject<ConfigTecla>("{" + entry.Value.Replace('|', '"') + "}");
                    if (!_sectorTeclas.ContainsKey(entry.Key))
                        _sectorTeclas.Add(entry.Key, Tecla);
                    else
                        _sectorTeclas[entry.Key] = Tecla;
                }
                catch
                {
                    _logger.Info("Error en la definicion de tecla: [{0}]", entry.Key.ToString());
                }
            }

            if (_sectorTeclas.ContainsKey("TeclaEnie"))
            {
                _TeclaEnie = ConvertKeyFromString(_sectorTeclas["TeclaEnie"].Tecla);
            }
        }
        #endregion

        #region Metodos de analisis de teclas presionadas
        /// <summary>
        /// Convierte del enumerado Key a un string
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string ConvertKeyToString(Key key)
        {
            switch (key)
            {
                case Key.D0:
                case Key.D1:
                case Key.D2:
                case Key.D3:
                case Key.D4:
                case Key.D5:
                case Key.D6:
                case Key.D7:
                case Key.D8:
                case Key.D9:
                    return ((int)key - 34).ToString();
                case Key.NumPad0:
                case Key.NumPad1:
                case Key.NumPad2:
                case Key.NumPad3:
                case Key.NumPad4:
                case Key.NumPad5:
                case Key.NumPad6:
                case Key.NumPad7:
                case Key.NumPad8:
                case Key.NumPad9:
                    return ((int)key - 74).ToString();
                case Key.A:
                case Key.B:
                case Key.C:
                case Key.D:
                case Key.E:
                case Key.F:
                case Key.G:
                case Key.H:
                case Key.I:
                case Key.J:
                case Key.K:
                case Key.L:
                case Key.M:
                case Key.N:
                case Key.O:
                case Key.P:
                case Key.Q:
                case Key.R:
                case Key.S:
                case Key.T:
                case Key.U:
                case Key.V:
                case Key.W:
                case Key.X:
                case Key.Y:
                case Key.Z:
                    return (key.ToString());
                case Key.LeftAlt:
                case Key.LeftCtrl:
                case Key.LeftShift:
                case Key.RightAlt:
                case Key.RightCtrl:
                case Key.RightShift:
                case Key.LWin:
                case Key.RWin:
                    return string.Empty;
                case Key.OemPeriod:
                case Key.Decimal:
                    return ".";
                case Key.Return:
                    return "Enter";
                case Key.Back:
                case Key.Escape:
                case Key.F1:
                case Key.F2:
                case Key.F3:
                case Key.F4:
                case Key.F5:
                case Key.F6:
                case Key.F7:
                case Key.F8:
                case Key.F9:
                case Key.F10:
                case Key.F11:
                case Key.F12:
                    return key.ToString();
                default:
                    if(key == _TeclaEnie)
                       return "Ñ";
                    break;
            }
            return key.ToString();
        }

        private static Key ConvertKeyFromString(string keystr)
        {
            Key key;
            Enum.TryParse(keystr, out key);
            return key;
        }

        private static ModifierKeys ConvertModifierKeyFromString(string keystr)
        {
            ModifierKeys key;
            Enum.TryParse(keystr, out key);
            return key;
        }

        /// <summary>
        /// Obtiene la tecla alfanumerica que fue presionada
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public static char? GetKeyAlphaNumericValue(Key e)
        {
            if (IsNumericKey(e) || IsAlphabeticKey(e))
            {
                string strAux = ConvertKeyToString(e);
                if (strAux != string.Empty && Keyboard.Modifiers == ModifierKeys.None)
                    return char.Parse(strAux.ToUpper());
            }
            return null;
        }

        /// <summary>
        /// Comprueba si la tecla ingresada corresponde a una funcion.
        /// </summary>
        /// <param name="e"></param>
        /// <param name="nombreTecla"></param>
        /// <returns></returns>
        public static bool IsFunctionKey(Key e, string nombreTecla)
        {
            try
            {
                if (Keyboard.Modifiers == ConvertModifierKeyFromString(_sectorTeclas[nombreTecla].Modificador) && e == ConvertKeyFromString(_sectorTeclas[nombreTecla].Tecla))
                    return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static string GetEtiquetaTecla(Key tecla, ModifierKeys modificador)
        {
            string nRet = string.Empty;

            try
            {
                nRet = _sectorTeclas.FirstOrDefault(x => x.Value.Tecla == ConvertKeyToString(tecla) && x.Value.Modificador == modificador.ToString()).Value.Etiqueta;
            }
            catch
            {
                nRet = string.Empty;
            }
            return nRet;
        }

        public static string GetEtiquetaTecla(string description)
        {
            string nRet = string.Empty;

            if (_sectorTeclas.ContainsKey(description))
                nRet = _sectorTeclas[description].Etiqueta;
            return nRet;
        }

        /// <summary>
        /// Comprueba si esta definida una tecla con la descripcion informada
        /// </summary>
        /// <param name="description"></param>
        /// <returns></returns>
        public static bool IsExistingKey(string description)
        {
            if (!_sectorTeclas.ContainsKey(description))
                return false;
            return true;
        }

        /// <summary>
        /// Obtiene la tecla numerica que fue presionada
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public static int GetKeyNumericValue(Key e)
        {
            int salida=0;
            bool nRet = false;

            if (IsNumericKey(e))
            {
                nRet = int.TryParse(ConvertKeyToString(e), out salida);
            }
            if (nRet)
                return salida;
            else
                return -1;
        }

        /// <summary>
        /// Comprueba si la tecla presionada es un numero o una letra minuscula
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public static bool IsLowerCaseOrNumberKey(Key e)
        {
            bool nRet = false;

            string strAux = ConvertKeyToString(e);
            if (strAux != string.Empty)
                nRet = true;
            return nRet;
        }

        public static bool IsDecimalKey(Key e)
        {
            if (e == Key.OemPeriod || e == Key.Decimal)
                return true;
            return false;
        }

        /// <summary>
        /// Comprueba si la tecla presionada es un numero
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public static bool IsNumericKey(Key e)
        {
            if((e >= Key.D0 && e<= Key.D9) || (e >= Key.NumPad0 && e <= Key.NumPad9))
                return true;
            return false;
        }

        public static bool IsAlphabeticKey(Key e)
        {
            if ((e >= Key.A && e <= Key.Z))
                return true;
            return false;
        }

        /// <summary>
        /// Comprueba si la tecla presionada es fecha arriba
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public static bool IsUpKey(Key e)
        {
            if (e == Key.Up)
                return true;
            return false;
        }

        /// <summary>
        /// Comprueba si la tecla presionada es flecha abajo
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public static bool IsDownKey(Key e)
        {
            if (e == Key.Down)
                return true;
            return false;
        }

        /// <summary>
        /// Comprueba si la tecla presionada es Backspace
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public static bool IsBackspaceKey(Key e)
        {
            if (_sectorTeclas.ContainsKey("Backspace"))
            {
                if (Keyboard.Modifiers == ConvertModifierKeyFromString(_sectorTeclas["Backspace"].Modificador) && e == ConvertKeyFromString(_sectorTeclas["Backspace"].Tecla))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Comprueba si se presiono la tecla escape
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public static bool IsEscapeKey(Key e)
        {
            if (_sectorTeclas.ContainsKey("Escape"))
            {
                if (Keyboard.Modifiers == ConvertModifierKeyFromString(_sectorTeclas["Escape"].Modificador) && e == ConvertKeyFromString(_sectorTeclas["Escape"].Tecla))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Comprueba si se presiono la tecla de confirmacion (enter)
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public static bool IsConfirmationKey(Key e)
        {
            if (_sectorTeclas.ContainsKey("Enter"))
            {
                if (Keyboard.Modifiers == ConvertModifierKeyFromString(_sectorTeclas["Enter"].Modificador) && e == ConvertKeyFromString(_sectorTeclas["Enter"].Tecla))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Comprueba si se presiono la tecla de efectivo
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public static bool IsCashKey(Key e)
        {
            if (_sectorTeclas.ContainsKey("Cash"))
            {
                if (Keyboard.Modifiers == ConvertModifierKeyFromString(_sectorTeclas["Cash"].Modificador) && e == ConvertKeyFromString(_sectorTeclas["Cash"].Tecla))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Comprueba si se presiono la tecla de siguiente pagina (para ventanas con muchas opciones)
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public static bool IsNextPageKey(Key e)
        {
            if (_sectorTeclas.ContainsKey("NextPage"))
            {
                if (Keyboard.Modifiers == ConvertModifierKeyFromString(_sectorTeclas["NextPage"].Modificador) && e == ConvertKeyFromString(_sectorTeclas["NextPage"].Tecla))
                    return true;
            }
            return false;
        }

        public static bool IsEnieKey(Key e)
        {
            if (_sectorTeclas.ContainsKey("TeclaEnie"))
            {
                if (Keyboard.Modifiers == ConvertModifierKeyFromString(_sectorTeclas["TeclaEnie"].Modificador) && e == ConvertKeyFromString(_sectorTeclas["TeclaEnie"].Tecla))
                    return true;
            }
            return false;
        }
        #endregion
    }
}
