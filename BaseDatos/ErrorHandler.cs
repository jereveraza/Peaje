using Entidades;
using Entidades.ComunicacionBaseDatos;
using Newtonsoft.Json;
using System.ComponentModel;

namespace ModuloBaseDatos
{
    public enum eErrorConfig
    {
        [Description("El valor está vacío, modifíquelo y reinicie el servicio.")]
        Vacio,
        [Description("El valor debe ser numérico, modifíquelo y reinicie el servicio.")]
        Numerico,
        [Description("El valor especificado es incorrecto, modifiquelo y reinicie el servicio")]
        Incorrecto,
        [Description("El valor especificado está fuera de rango, modifiquelo y reinicie el servicio")]
        Rango
    }

    public class ErrorHandler
    {
        /// ****************************************************************************************************
        /// <summary>
        /// Arma respuesta en formato string para ser enviada al cliente
        /// </summary>
        /// <param name="error">Error a generar</param>
        /// <returns>Respuesta en base al error</returns>
        /// ****************************************************************************************************
        public static string ArmaRespuestaError(EnmErrorBaseDatos error)
        {
            RespuestaBaseDatos respuesta = new RespuestaBaseDatos();
            string sError;
            respuesta.CodError = error;
            respuesta.DescError = Utility.ObtenerDescripcionEnum(error);
            sError = JsonConvert.SerializeObject(respuesta);

            return sError;
        }
    }
}
