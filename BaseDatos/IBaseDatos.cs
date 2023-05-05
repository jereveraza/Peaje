using Entidades;
using Entidades.ComunicacionBaseDatos;
using ModuloBaseDatos.Entidades;
using System;
using System.Threading.Tasks;

namespace ModuloBaseDatos
{
    /// ****************************************************************************************************
    /// <summary>
    /// Enumerado de los diferentes motores de Base de Datos que puede gestionar el servicio.
    /// </summary>
    /// ****************************************************************************************************
    public enum MotorBaseDatos
    {
        SQLITE, SQLEXPRESS, SQLEXPRESS2014, INICIO
    }

    /// ****************************************************************************************************
    /// <summary>
    /// Clase interfaz que contribuye a la gestión del motor de base de datos.
    /// </summary>
    /// ****************************************************************************************************
    public interface IBaseDatos
    {
        Task<RespuestaBaseDatos> ConsultaBaseLocal(SolicitudBaseDatos parametros);

        Task<RespuestaBaseDatos> ConsultaBaseEstacion(SolicitudBaseDatos parametros);

        string ProcesarConsulta(SolicitudBaseDatos consulta);

        EnmErrorBaseDatos Exceptions(int eNumer, string sError);

        void Init(ConfiguracionBaseDatos config);

        bool BuscarConfiguracion(string sOpcion);

        string Connection { get; set; }

        void ClearPools();
    }
}
