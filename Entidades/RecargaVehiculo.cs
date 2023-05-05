
namespace ModuloPantallaTeclado.Entidades
{
    public class RecargaVehiculo
    {
        private string m_sPatente, m_sNumeroTag,m_sMarca,m_sModelo,m_sColor,m_sNombre;
        private int m_nCategoria;
        private string m_lOpcionesRecarga;

        public string Patente { get { return m_sPatente; } set { m_sPatente = value; } }
        public string NumeroTag { get { return m_sNumeroTag; } set { m_sNumeroTag = value; } }
        public string Marca { get { return m_sMarca; } set { m_sMarca = value; } }
        public string Modelo { get { return m_sModelo; } set { m_sModelo = value; } }
        public string Color { get { return m_sColor; } set { m_sColor = value; } }
        public string Nombre { get { return m_sNombre; } set { m_sNombre = value; } }
        public int Categoria { get { return m_nCategoria; } set { m_nCategoria = value; } }
        public string OpcionesRecarga { get { return m_lOpcionesRecarga; } set { m_lOpcionesRecarga = value; } }
    }
}
