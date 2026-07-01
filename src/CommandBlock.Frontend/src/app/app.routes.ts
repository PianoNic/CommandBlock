import { Routes } from '@angular/router';
import { autoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';
import { AppLayout } from './shared/layouts/app-layout/app-layout';
import { Home } from './home/home';
import { Servers } from './servers/servers';
import { Console } from './console/console';
import { Files } from './files/files';
import { Backups } from './backups/backups';
import { Activity } from './activity/activity';

export const routes: Routes = [
  {
    path: '',
    component: AppLayout,
    canActivateChild: [autoLoginPartialRoutesGuard],
    children: [
      { path: '', component: Home },
      { path: 'servers', component: Servers },
      { path: 'console/:id', component: Console },
      { path: 'files/:id', component: Files },
      { path: 'backups', component: Backups },
      { path: 'activity', component: Activity },
    ],
  },
  { path: '**', redirectTo: '' },
];
