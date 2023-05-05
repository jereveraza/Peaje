using System;
using System.Text;
using Utiles;

namespace ModuloPantallaTeclado.Clases
{
    public static class Datos
    {
        #region Variables y propiedades de clase
        private static readonly object obj = new object();
        private static string _simboloMoneda = null;
        private static int _cantidadDecimales;
        private static StringBuilder _sbFormato = new StringBuilder();
        #endregion

        #region Constantes y definiciones globales
        public const double AnchoFoto = 494;//450;
        public const double AltoFoto = 370;//325;
        #endregion

        #region Constructor de la clase
        static Datos()
        {
            if(string.IsNullOrEmpty(_simboloMoneda))
            {
                string pathConfigLogica;

                pathConfigLogica = ClassUtiles.LeerConfiguracion("Idioma", "PATH_CONFIG_LOGICA");

                if (string.IsNullOrEmpty(pathConfigLogica))
                {
                    _simboloMoneda = ClassUtiles.LeerConfiguracion("DATOS", "SimboloMoneda");
                    _cantidadDecimales = Convert.ToInt32(ClassUtiles.LeerConfiguracion("DATOS", "CantidadDecimales"));
                }
                else   //Estoy en pantalla, cargo la configuracion desde lógica
                {
                    AppDomain.CurrentDomain.SetData("APP_CONFIG_FILE", pathConfigLogica);
                    ClassUtiles.ResetConfigMechanism();

                    _simboloMoneda = ClassUtiles.LeerConfiguracion("DATOS", "SimboloMoneda");
                    _cantidadDecimales = Convert.ToInt32(ClassUtiles.LeerConfiguracion("DATOS", "CantidadDecimales"));

                    AppDomain.CurrentDomain.SetData("APP_CONFIG_FILE", @"./TCP-TOLL-WINDOWS.exe.config");
                    ClassUtiles.ResetConfigMechanism();
                    
                }
            }

            if (_cantidadDecimales > 0)
            {
                _sbFormato.Append('0');
                _sbFormato.Append(".");
                _sbFormato.Append('0', _cantidadDecimales);
            }
            else
                _sbFormato.Append('0');
        }
        #endregion

        #region Metodos para gestion de monedas
        public static string GetSimboloMonedaReferencia()
        {
            return _simboloMoneda + " ";
        }

        public static int GetCantidadDecimales()
        {
            return _cantidadDecimales;
        }

        public static string FormatearMonedaAString(decimal valor, string simboloMoneda = "")
        {
            string sValor, simboloM;
            
            sValor = valor.ToString(_sbFormato.ToString());

            simboloM = (simboloMoneda=="")?_simboloMoneda:simboloMoneda;

            return string.Format(simboloM + " " + sValor);
        }
        #endregion
    
    }
}
