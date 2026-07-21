import { ServerInstanceDto } from '../../api/model/serverInstanceDto';

/// The address a player types for this server. A routed server is reached by hostname through the
/// shared port; a direct one only exists at <host>:<port> and has no hostname at all - so every place
/// that used to print `hostname` has to ask for this instead, or it renders a blank cell.
/// An unbound port listens on every interface, which `*` is the conventional shorthand for.
export function serverAddress(
  s: Pick<ServerInstanceDto, 'hostname' | 'lanPort' | 'lanBindAddress' | 'routedThroughProxy'>,
): string {
  if (s.routedThroughProxy === false && s.lanPort) {
    return `${s.lanBindAddress?.trim() || '*'}:${s.lanPort}`;
  }
  return s.hostname?.trim() || '-';
}
