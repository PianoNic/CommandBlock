import { ChangeDetectionStrategy, Component, model } from '@angular/core';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmLabelImports } from '@spartan-ng/helm/label';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { HlmCheckboxImports } from '@spartan-ng/helm/checkbox';

/// Shared JVM / Java-runtime fields, reused by the create dialog and the runtime editor. Values are
/// two-way bound via model() signals; the parent decides what to do with them.
@Component({
  selector: 'app-server-runtime-fields',
  imports: [HlmInputImports, HlmLabelImports, HlmSelectImports, HlmCheckboxImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'grid grid-cols-2 gap-3' },
  template: `
    <div class="flex flex-col gap-1.5">
      <label hlmLabel for="rt-java" class="text-muted-foreground text-xs uppercase tracking-wide">Java runtime</label>
      <hlm-select [value]="javaVersion()" (valueChange)="javaVersion.set($event ?? 'auto')" [itemToString]="javaLabel">
        <hlm-select-trigger id="rt-java" class="w-full"><hlm-select-value placeholder="Auto" /></hlm-select-trigger>
        <hlm-select-content *hlmSelectPortal>
          <hlm-select-item value="auto">Auto (from version)</hlm-select-item>
          <hlm-select-item value="21">Java 21</hlm-select-item>
          <hlm-select-item value="17">Java 17</hlm-select-item>
          <hlm-select-item value="11">Java 11</hlm-select-item>
          <hlm-select-item value="8">Java 8</hlm-select-item>
        </hlm-select-content>
      </hlm-select>
    </div>

    <div class="flex flex-col gap-1.5">
      <label hlmLabel class="text-muted-foreground text-xs uppercase tracking-wide">GC flags</label>
      <div class="border-input flex h-9 items-center gap-2 rounded-md border px-3 text-sm">
        <hlm-checkbox [checked]="useAikarFlags()" (checkedChange)="useAikarFlags.set($event)" />
        <span class="cursor-pointer" (click)="useAikarFlags.set(!useAikarFlags())">Aikar's optimized flags</span>
      </div>
    </div>

    <div class="col-span-2 flex flex-col gap-1.5">
      <label hlmLabel for="rt-jvm" class="text-muted-foreground text-xs uppercase tracking-wide">JVM arguments</label>
      <input
        hlmInput
        id="rt-jvm"
        class="font-mono text-xs"
        placeholder="-XX:+UseG1GC -Dsome.prop=value"
        [value]="jvmArgs()"
        (input)="jvmArgs.set($any($event.target).value)"
      />
    </div>

    <div class="col-span-2 flex flex-col gap-1.5">
      <label hlmLabel for="rt-env" class="text-muted-foreground text-xs uppercase tracking-wide">Extra environment variables</label>
      <textarea
        hlmInput
        id="rt-env"
        rows="6"
        class="resize-y py-2 font-mono text-xs leading-5"
        style="field-sizing: fixed; height: 8rem"
        placeholder="VIEW_DISTANCE=10&#10;ENABLE_ROLLING_LOGS=true"
        [value]="extraEnv()"
        (input)="extraEnv.set($any($event.target).value)"
      ></textarea>
      <span class="text-muted-foreground text-xs">One <span class="font-mono">KEY=VALUE</span> per line - sets itzg env directly.</span>
    </div>
  `,
})
export class ServerRuntimeFields {
  readonly javaVersion = model<string>('auto');
  readonly useAikarFlags = model<boolean>(false);
  readonly jvmArgs = model<string>('');
  readonly extraEnv = model<string>('');

  protected readonly javaLabel = (v: string | null): string => {
    switch (v) {
      case '21': return 'Java 21';
      case '17': return 'Java 17';
      case '11': return 'Java 11';
      case '8': return 'Java 8';
      default: return 'Auto (from version)';
    }
  };
}
