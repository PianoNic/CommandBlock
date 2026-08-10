namespace CommandBlock.Domain
{
    /// <summary>A root domain the user has pointed at this server (e.g. "pianonic.ch"). Servers pick
    /// one of these plus a subdomain to form their routing hostname, so the UI can offer the domain
    /// as a dropdown instead of asking for the full hostname every time. The DNS/firewall setup for
    /// a domain is a one-time step the user does at their registrar - see the Settings tutorial.</summary>
    public class DomainEntry : BaseEntity
    {
        /// <summary>The bare root domain, lower-cased and without scheme or trailing dot,
        /// e.g. "pianonic.ch". Unique.</summary>
        public required string Name { get; set; }
    }
}
