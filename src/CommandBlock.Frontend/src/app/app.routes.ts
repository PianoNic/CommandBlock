import { Routes } from '@angular/router';
import { autoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';
import { AppLayout } from './shared/layouts/app-layout/app-layout';
import { Servers } from './servers/servers';
import { Activity } from './activity/activity';

export const routes: Routes = [
  {
    path: '',
    component: AppLayout,
    canActivateChild: [autoLoginPartialRoutesGuard],
    children: [
      { path: '', component: Servers },
      { path: 'servers', component: Servers },
      { path: 'activity', component: Activity },
    ],
  },
  { path: '**', redirectTo: '' },
];
