
namespace ModuloPantallaTeclado.Entidades
{
    public class TicketManual
    {
        private string m_sNroTicket, m_sNroPtoVenta;

        public string Numero { get { return m_sNroTicket; } set { m_sNroTicket = value; } }
        public string PuntoVenta { get { return m_sNroPtoVenta; } set { m_sNroPtoVenta = value; } }

        public TicketManual()
        {

        }

        public TicketManual(string ticket, string ptoVenta)
        {
            m_sNroTicket = ticket;
            m_sNroPtoVenta = ptoVenta;
        }
    }
}
