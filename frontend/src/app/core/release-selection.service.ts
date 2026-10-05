import { Injectable, signal, computed } from '@angular/core';
import { ReleaseDto } from './models';

@Injectable({ providedIn: 'root' })
export class ReleaseSelectionService {
  readonly releases = signal<ReleaseDto[]>([]);
  readonly previousReleaseId = signal<number | null>(null);
  readonly currentReleaseId = signal<number | null>(null);

  readonly previousRelease = computed(() =>
    this.releases().find((r) => r.id === this.previousReleaseId()) ?? null,
  );
  readonly currentRelease = computed(() =>
    this.releases().find((r) => r.id === this.currentReleaseId()) ?? null,
  );
  readonly hasSelection = computed(
    () => this.previousReleaseId() !== null && this.currentReleaseId() !== null,
  );

  setReleases(releases: ReleaseDto[]) {
    this.releases.set(releases);
    // Default: oldest two -> previous, and newest -> current, if not already chosen.
    if (releases.length >= 2 && (this.previousReleaseId() === null || this.currentReleaseId() === null)) {
      const sorted = [...releases].sort((a, b) => a.releaseDate.localeCompare(b.releaseDate));
      this.previousReleaseId.set(sorted[sorted.length - 2].id);
      this.currentReleaseId.set(sorted[sorted.length - 1].id);
    }
  }

  setPrevious(id: number) {
    this.previousReleaseId.set(id);
  }

  setCurrent(id: number) {
    this.currentReleaseId.set(id);
  }
}
