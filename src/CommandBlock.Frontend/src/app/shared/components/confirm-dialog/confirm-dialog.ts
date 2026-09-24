import { ChangeDetectionStrategy, Component, computed, inject, Injectable, signal } from '@angular/core';
import { BrnDialogRef, injectBrnDialogContext } from '@spartan-ng/brain/dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialogDescription, HlmDialogHeader, HlmDialogService, HlmDialogTitle } from '@spartan-ng/helm/dialog';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';

export type ConfirmDialogContext = {
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
};

export type PromptDialogContext = {
  title: string;
  message?: string;
  label: string;
  value?: string;
  placeholder?: string;
  confirmLabel?: string;
  /// Returns an error to show under the field, or null when the value is acceptable.
  validate?: (value: string) => string | null;
};

@Component({
  selector: 'app-confirm-dialog',
  imports: [HlmButtonImports, HlmDialogHeader, HlmDialogTitle, HlmDialogDescription],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>{{ ctx.title }}</h3>
      <p hlmDialogDescription>{{ ctx.message }}</p>
    </hlm-dialog-header>

    <div class="flex justify-end gap-2">
      <button hlmBtn variant="outline" type="button" (click)="cancel()">
        {{ ctx.cancelLabel ?? 'Cancel' }}
      </button>
      <button
        hlmBtn
        type="button"
        [variant]="ctx.destructive ? 'destructive' : 'default'"
        (click)="confirm()"
      >
        {{ ctx.confirmLabel ?? 'Confirm' }}
      </button>
    </div>
  `,
})
export class ConfirmDialog {
  protected readonly ctx = injectBrnDialogContext<ConfirmDialogContext>();
  private readonly ref = inject(BrnDialogRef);

  protected confirm(): void { this.ref.close(true); }
  protected cancel(): void { this.ref.close(false); }
}

/// Single-field input dialog: the in-app replacement for window.prompt().
@Component({
  selector: 'app-prompt-dialog',
  imports: [HlmButtonImports, HlmDialogHeader, HlmDialogTitle, HlmDialogDescription, HlmInputImports, HlmLabelImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-4' },
  template: `
    <hlm-dialog-header>
      <h3 hlmDialogTitle>{{ ctx.title }}</h3>
      @if (ctx.message) { <p hlmDialogDescription>{{ ctx.message }}</p> }
    </hlm-dialog-header>

    <form class="flex flex-col gap-4" (submit)="$event.preventDefault(); submit()">
      <div class="flex flex-col gap-1.5">
        <label hlmLabel for="prompt-input">{{ ctx.label }}</label>
        <input
          hlmInput
          id="prompt-input"
          autocomplete="off"
          [value]="value()"
          [placeholder]="ctx.placeholder ?? ''"
          [attr.aria-invalid]="error() ? true : null"
          aria-describedby="prompt-error"
          (input)="value.set($any($event.target).value)"
          autofocus
        />
        @if (error(); as e) { <p id="prompt-error" class="text-destructive text-xs">{{ e }}</p> }
      </div>
      <div class="flex justify-end gap-2">
        <button hlmBtn variant="outline" type="button" (click)="cancel()">Cancel</button>
        <button hlmBtn type="submit" [disabled]="!value().trim()">{{ ctx.confirmLabel ?? 'Save' }}</button>
      </div>
    </form>
  `,
})
export class PromptDialog {
  protected readonly ctx = injectBrnDialogContext<PromptDialogContext>();
  private readonly ref = inject(BrnDialogRef);
  protected readonly value = signal(this.ctx.value ?? '');
  private readonly touched = signal(false);
  protected readonly error = computed(() => (this.touched() ? (this.ctx.validate?.(this.value().trim()) ?? null) : null));

  protected submit(): void {
    this.touched.set(true);
    const v = this.value().trim();
    if (!v || this.ctx.validate?.(v)) return;
    this.ref.close(v);
  }
  protected cancel(): void { this.ref.close(null); }
}

@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialog = inject(HlmDialogService);

  open(ctx: ConfirmDialogContext): Promise<boolean> {
    return new Promise((resolve) => {
      // Alert-dialog semantics: don't dismiss on backdrop click - the user must
      // explicitly press Cancel or Confirm so destructive actions can't slip past.
      const ref = this.dialog.open(ConfirmDialog, {
        context: ctx,
        contentClass: 'sm:max-w-md',
        closeOnBackdropClick: false,
        closeOnOutsidePointerEvents: false,
      });
      ref.closed$.subscribe((result) => resolve(result === true));
    });
  }

  /// Asks for one line of text. Resolves with the trimmed value, or null when cancelled.
  prompt(ctx: PromptDialogContext): Promise<string | null> {
    return new Promise((resolve) => {
      const ref = this.dialog.open(PromptDialog, { context: ctx, contentClass: 'sm:max-w-md' });
      ref.closed$.subscribe((result) => resolve(typeof result === 'string' ? result : null));
    });
  }
}
