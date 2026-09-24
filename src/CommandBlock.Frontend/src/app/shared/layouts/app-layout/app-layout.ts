import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { HlmToaster } from '@spartan-ng/helm/sonner';
import { ThemeService } from '../../services/theme.service';
import { Sidenav } from '../../../sidenav/sidenav';

@Component({
  selector: 'app-app-layout',
  imports: [RouterOutlet, HlmSidebarImports, HlmToaster, Sidenav],
  templateUrl: './app-layout.html',
})
export class AppLayout {
  protected readonly theme = inject(ThemeService);
}
