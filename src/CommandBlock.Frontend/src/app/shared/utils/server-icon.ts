import { environment } from '../environments/environment';

/// The uploaded icon for a server. Pass a version to bust the browser cache after an upload.
export function serverIconUrl(serverId: string, version?: number): string {
  const url = `${environment.apiBaseUrl}/api/Server/${serverId}/icon`;
  return version === undefined ? url : `${url}?v=${version}`;
}
