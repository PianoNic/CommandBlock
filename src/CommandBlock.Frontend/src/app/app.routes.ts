import { Routes } from '@angular/router';
import { autoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';
import { AppLayout } from './shared/layouts/app-layout/app-layout';

// Feature routes are lazy so heavy, route-specific deps (xterm for the console, CodeMirror for the
// file editor) stay out of the initial bundle and only download when their route is visited.
export const routes: Routes = [
  {
    path: '',
    component: AppLayout,
    canActivateChild: [autoLoginPartialRoutesGuard],
    children: [
      { path: '', loadComponent: () => import('./home/home').then((m) => m.Home) },
      { path: 'servers', loadComponent: () => import('./servers/servers').then((m) => m.Servers) },
      { path: 'servers/:id', loadComponent: () => import('./servers/server-detail').then((m) => m.ServerDetail) },
      { path: 'files/:id', loadComponent: () => import('./files/files').then((m) => m.Files) },
      { path: 'backups', loadComponent: () => import('./backups/backups').then((m) => m.Backups) },
      { path: 'activity', loadComponent: () => import('./activity/activity').then((m) => m.Activity) },
      { path: 'connections', loadComponent: () => import('./connections/connections').then((m) => m.Connections) },
      { path: 'settings', loadComponent: () => import('./settings/settings').then((m) => m.Settings) },
    ],
  },
  { path: '**', redirectTo: '' },
];
