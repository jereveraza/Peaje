using System.Collections.Generic;

namespace ModuloPantallaTeclado.Entidades
{
    //Mensajes que van a supervision
    public class MensajeSupervision
    {
        private string m_sMensaje;
        private int m_iCodigo;
        private int m_iCodOrden;

        public string Mensaje { get { return m_sMensaje; } set { m_sMensaje = value; } }
        public int Codigo { get { return m_iCodigo; } set { m_iCodigo = value; } }
        public int Orden { get { return m_iCodOrden; } set { m_iCodOrden = value; } }

        public MensajeSupervision()
        {

        }

        public MensajeSupervision(int codigo, string mensaje, int orden)
        {
            m_sMensaje = mensaje;
            m_iCodigo = codigo;
            m_iCodOrden = orden;
        }
    }

    public class ListadoMsgSupervision
    {
        private List<MensajeSupervision> m_lListaMensajes = new List<MensajeSupervision>();

        public List<MensajeSupervision> ListaMensajes
        {
            get
            {
                m_lListaMensajes.Sort((x, y) => x.Codigo.CompareTo(y.Orden));
                return m_lListaMensajes;
            }
            set
            {
                m_lListaMensajes = value;
            }
        }
    }
}
