import { Component, effect, ElementRef, HostListener, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService, downloadBlob } from '../../core/api.service';
import { ReleaseSelectionService } from '../../core/release-selection.service';
import { EntrySummaryDto, LotamEntryDetailDto, MaterialSectionOptionDto } from '../../core/models';
import { SeverityBadgeComponent } from '../../shared/severity-badge/severity-badge.component';
import { ExportLoadingComponent } from '../../shared/export-loading/export-loading.component';
import { ExportDownloadEvent, ExportEmailEvent, ExportMenuComponent } from '../../shared/export-menu/export-menu.component';
import { ToastService } from '../../core/toast.service';

type ReleaseTab = 'previous' | 'current';

// Which of the two Material Explorer tabs is active. 'flat' is the
// original view (unchanged); 'hierarchy' is the new Material -> Equipment
// Description -> Supplier tree. Both render the exact same `rows` data -
// this is purely a different organization of it, not a separate dataset.
type ViewMode = 'flat' | 'hierarchy';

// One "SER {n}" group in the hierarchy view - e.g. group 1 = "CONDUCTORS,
// BARE", containing sections 1.1, 1.2, 1.3... Grouped by the leading
// integer of materialCode (same numbering SEC itself uses), not a separate
// lookup - the category name is already carried on every row.
interface CategoryGroup {
  groupKey: number;
  categoryName: string;
  sections: SectionGroup[];
}

// One material_section (e.g. "1.1") within a CategoryGroup - the
// "EQUIPMENT DESCRIPTION" level, with its own applicable standard and the
// same supplier rows Tab 1 shows for this material code.
interface SectionGroup {
  code: string;
  description: string;
  applicableStandard: string | null;
  rows: EntrySummaryDto[];
}

@Component({
  selector: 'app-material-explorer',
  standalone: true,
  imports: [CommonModule, FormsModule, SeverityBadgeComponent, ExportLoadingComponent, ExportMenuComponent],
  template: `
    <div class="page" *ngIf="selection.hasSelection(); else noSelection">
      <h1>Material Explorer</h1>
      <p class="subtitle">
        Every supplier record in this release, listed below by default. Check one or more material
        groups in the filter to narrow it down, and click any row to see its full detail exactly as
        SEC printed it.
      </p>

      <div class="tabs">
        <button [class.active]="activeReleaseTab === 'previous'" (click)="switchReleaseTab('previous')">
          {{ selection.previousRelease()?.releaseDate }} — {{ selection.previousRelease()?.sourceFilename }}
        </button>
        <button [class.active]="activeReleaseTab === 'current'" (click)="switchReleaseTab('current')">
          {{ selection.currentRelease()?.releaseDate }} — {{ selection.currentRelease()?.sourceFilename }}
        </button>
      </div>

      <div class="view-tabs">
        <button [class.active]="activeView === 'flat'" (click)="activeView = 'flat'">Existing View</button>
        <button [class.active]="activeView === 'hierarchy'" (click)="activeView = 'hierarchy'">New Hierarchy</button>
      </div>

      <div class="card combobox-card" #comboboxCard>
        <label class="combo-label">Filter by material group (optional - leave everything unchecked to see all)</label>
        <div class="combobox">
          <input
            type="text"
            placeholder="Search to narrow this checklist (category, description, standard)..."
            [(ngModel)]="searchTerm"
            (focus)="dropdownOpen = true"
            (input)="dropdownOpen = true"
          />
          <div class="combo-list" *ngIf="dropdownOpen && filteredOptions().length > 0">
            <label class="combo-item" *ngFor="let o of filteredOptions()">
              <input type="checkbox" [checked]="isChecked(o)" (change)="toggleChecked(o)" (click)="$event.stopPropagation()" />
              <span class="combo-code">{{ o.code }}</span>
              <span class="combo-label-text">{{ o.label }}</span>
            </label>
          </div>
        </div>

        <div class="filter-summary" *ngIf="selectedCodes.size > 0">
          <span class="muted">{{ selectedCodes.size }} material group(s) selected</span>
          <button (click)="clearFilter()">Clear filter</button>
        </div>
      </div>

      <div class="toolbar">
        <span class="muted">
          {{ rows.length }} supplier record(s){{ selectedCodes.size > 0 ? ' (filtered)' : '' }}
        </span>
        <div class="toolbar-actions">
          <button (click)="expandAll()">Expand all</button>
          <button (click)="collapseAll()">Collapse all</button>
          <app-export-menu
            [disabled]="selectedCodes.size === 0"
            [disabledTitle]="selectedCodes.size > 0 ? 'Export the selected materials' : 'Select at least one material in the filter to enable export'"
            (downloadRequested)="onExportDownload($event)"
            (emailRequested)="onExportEmail($event)"
          ></app-export-menu>
        </div>
      </div>

      <div class="muted" *ngIf="loading" style="padding: 16px 0;">Loading...</div>

      <!-- ============ TAB 1: Existing flat view - unchanged ============ -->
      <div class="accordion" *ngIf="!loading && activeView === 'flat'">
        <div class="card row-card" *ngFor="let r of rows">
          <div class="row-header" (click)="toggleExpand(r)">
            <span class="chevron" [class.open]="isExpanded(r)">▸</span>
            <span class="combo-code">{{ r.materialCode }}</span>
            <span class="supplier-name">{{ r.supplierName }}</span>
            <span class="material-label muted">{{ r.materialLabel }}</span>
            <app-severity-badge [severity]="r.status" [statusMode]="true" [class.changed-ring]="isExpanded(r) && isSectionChanged(r, 'status')"></app-severity-badge>
            <span class="badge badge-emerald" *ngIf="r.badge === 'New'">NEW</span>
            <span class="badge badge-rose" *ngIf="r.badge === 'Removed'">REMOVED</span>
            <span class="badge badge-amber" *ngIf="r.badge === 'Updated'">UPDATED</span>
          </div>

          <div class="row-body" *ngIf="isExpanded(r)">
            <ng-container *ngTemplateOutlet="entryDetail; context: { r: r }"></ng-container>
          </div>
        </div>

        <div class="card" *ngIf="rows.length === 0">
          <p class="muted">No supplier records match this filter.</p>
        </div>
      </div>

      <!-- ============ TAB 2: New Material -> Equipment Description -> Supplier hierarchy ============ -->
      <div *ngIf="!loading && activeView === 'hierarchy'">
        <div class="card hierarchy-search-card">
          <label class="combo-label">Search this hierarchy (material, description, standard, supplier)</label>
          <div class="hierarchy-search-row">
            <input
              type="text"
              placeholder="Type to filter and auto-expand matching branches..."
              [(ngModel)]="hierarchySearchTerm"
              (ngModelChange)="onHierarchySearchChange()"
            />
            <button (click)="clearHierarchySearch()" [disabled]="!hierarchySearchTerm">Clear search</button>
          </div>
        </div>

        <div class="hierarchy" *ngIf="filteredHierarchy().length > 0; else noHierarchyResults">
          <div class="card cat-card" *ngFor="let cat of filteredHierarchy()">
            <div class="cat-header" (click)="toggleCategory(cat)">
              <span class="chevron" [class.open]="isCategoryExpanded(cat)">▸</span>
              <span class="combo-code">{{ cat.groupKey }}</span>
              <span class="cat-name">{{ cat.categoryName }}</span>
              <span class="muted">{{ cat.sections.length }} material(s)</span>
            </div>

            <div class="cat-body" *ngIf="isCategoryExpanded(cat)">
              <div class="sec-card" *ngFor="let sec of cat.sections">
                <div class="sec-header" (click)="toggleSection(sec)">
                  <span class="chevron" [class.open]="isSectionExpanded(sec)">▸</span>
                  <span class="combo-code">{{ sec.code }}</span>
                  <span class="sec-desc">{{ sec.description }}</span>
                  <span class="muted sec-standard" *ngIf="sec.applicableStandard">Applicable Standards: {{ sec.applicableStandard }}</span>
                  <span class="muted">{{ sec.rows.length }} supplier(s)</span>
                </div>

                <div class="sec-body" *ngIf="isSectionExpanded(sec)">
                  <div class="card row-card" *ngFor="let r of sec.rows">
                    <div class="row-header" (click)="toggleExpand(r)">
                      <span class="chevron" [class.open]="isExpanded(r)">▸</span>
                      <span class="supplier-name">{{ r.supplierName }}</span>
                      <app-severity-badge [severity]="r.status" [statusMode]="true" [class.changed-ring]="isExpanded(r) && isSectionChanged(r, 'status')"></app-severity-badge>
                      <span class="badge badge-emerald" *ngIf="r.badge === 'New'">NEW</span>
                      <span class="badge badge-rose" *ngIf="r.badge === 'Removed'">REMOVED</span>
                      <span class="badge badge-amber" *ngIf="r.badge === 'Updated'">UPDATED</span>
                    </div>

                    <div class="row-body" *ngIf="isExpanded(r)">
                      <ng-container *ngTemplateOutlet="entryDetail; context: { r: r }"></ng-container>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <ng-template #noHierarchyResults>
          <div class="card"><p class="muted">No materials match this search.</p></div>
        </ng-template>
      </div>

      <!-- Shared supplier-detail body - identical for both tabs, so the
           two views can never visually drift apart. -->
      <ng-template #entryDetail let-r="r">
        <div *ngIf="!detailFor(r)" class="muted" style="padding: 8px 0;">Loading...</div>

        <ng-container *ngIf="detailFor(r) as detail">
          <h3>Manufacturer Name, Plant, Location/Country, Address, Website, Telephone &amp; E-mail</h3>
          <div class="value-box" [class.changed]="isSectionChanged(r, 'manufacturer')">{{ detail.rawManufacturerBlock }}</div>

          <div class="two-col">
            <div>
              <h3>Authorized Local Representative/Vendor</h3>
              <div class="value-box" [class.changed]="isSectionChanged(r, 'rep')">{{ detail.localRepresentativeRaw || '—' }}</div>
            </div>
            <div>
              <h3>Type/Model and Rating</h3>
              <div class="value-box" [class.changed]="isSectionChanged(r, 'model')">{{ detail.modelRatingRaw || '—' }}</div>
            </div>
          </div>

          <h3>Type Tests Details</h3>
          <div class="value-box">{{ detail.typeTestsRaw || '—' }}</div>

          <h3>Remarks/Observations</h3>
          <div class="value-box" [class.changed]="isSectionChanged(r, 'remarks')">{{ detail.remarksRaw || '—' }}</div>
        </ng-container>
      </ng-template>
    </div>

    <app-export-loading [active]="exporting"></app-export-loading>

    <ng-template #noSelection>
      <div class="page">
        <h1>Material Explorer</h1>
        <div class="card">Select a release pair from the top bar first.</div>
      </div>
    </ng-template>
  `,
  styles: [
    `
      .subtitle { color: var(--text-dim); max-width: 820px; margin-bottom: 16px; }
      .muted { color: var(--text-dim); font-size: 12px; }
      .tabs { display: flex; gap: 8px; margin-bottom: 16px; }
      .tabs button.active {
        background: linear-gradient(135deg, var(--accent), var(--accent-2));
        border: none; color: white;
      }
      .view-tabs { display: flex; gap: 8px; margin-bottom: 16px; }
      .view-tabs button {
        border-radius: 999px; padding: 6px 16px; font-size: 12.5px; font-weight: 600;
        border: 1px solid var(--border-strong); background: transparent; color: var(--text-dim);
      }
      .view-tabs button.active {
        background: linear-gradient(135deg, var(--accent), var(--accent-2));
        border-color: transparent; color: white;
      }
      .combobox-card { position: relative; overflow: visible; z-index: 30; }
      .combo-label { display: block; font-size: 11px; color: var(--text-dim); text-transform: uppercase; letter-spacing: 0.06em; margin-bottom: 6px; }
      .combobox { position: relative; }
      .combobox input[type='text'] { width: 100%; }
      .combo-list {
        position: absolute; top: calc(100% + 6px); left: 0; right: 0; z-index: 20;
        background: rgba(19,28,46,0.96); backdrop-filter: blur(20px); -webkit-backdrop-filter: blur(20px);
        border: 1px solid var(--border-strong); border-radius: var(--radius-sm);
        max-height: 360px; overflow-y: auto; box-shadow: var(--shadow);
      }
      .combo-item { display: flex; gap: 10px; align-items: center; padding: 9px 12px; cursor: pointer; font-size: 13px; }
      .combo-item:hover { background: rgba(255,255,255,0.06); }
      .combo-item input[type='checkbox'] { flex-shrink: 0; accent-color: var(--accent); width: 15px; height: 15px; }
      .combo-code { font-family: var(--font-display); font-weight: 600; color: var(--accent); flex-shrink: 0; }
      .combo-label-text { flex: 1; color: var(--text); }
      .filter-summary { display: flex; align-items: center; gap: 10px; margin-top: 10px; }

      .toolbar { display: flex; justify-content: space-between; align-items: center; margin: 16px 0 10px; }
      .toolbar-actions { display: flex; gap: 8px; }

      .accordion { display: flex; flex-direction: column; gap: 8px; }
      .row-card { padding: 0; overflow: hidden; }
      .row-header {
        display: flex; align-items: center; gap: 12px; padding: 12px 18px; cursor: pointer;
        transition: background 150ms ease;
      }
      .row-header:hover { background: rgba(255,255,255,0.04); }
      .chevron { display: inline-block; transition: transform 150ms ease; color: var(--text-dim); flex-shrink: 0; }
      .chevron.open { transform: rotate(90deg); }
      .supplier-name { font-weight: 600; font-size: 13.5px; flex-shrink: 0; max-width: 260px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .material-label { flex: 1; font-size: 12px; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .row-body { padding: 4px 18px 18px; border-top: 1px solid var(--border); margin-top: 2px; padding-top: 14px; }
      .row-body h3 { margin-top: 0; margin-bottom: 8px; }
      .two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 16px; }
      .value-box {
        background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.08);
        border-radius: 10px; padding: 10px 12px; font-size: 13px; margin: 0 0 16px; white-space: pre-wrap; line-height: 1.5;
      }
      .value-box:last-child { margin-bottom: 0; }
      .value-box.changed {
        background: var(--hue-amber-bg);
        border-color: var(--hue-amber-border);
        box-shadow: 0 0 0 1px var(--hue-amber-border);
      }
      /* Status badge uses a ring instead of a background swap, since it's
         already colored by its own status - a ring layers on top without
         fighting that color. Applied to app-severity-badge's host element
         directly (it's in this component's own template), no ::ng-deep
         needed to reach it. */
      .changed-ring {
        box-shadow: 0 0 0 2px var(--hue-amber) !important;
        border-radius: 999px;
      }

      /* ---- New Hierarchy tab ---- */
      .hierarchy-search-card { margin-bottom: 16px; }
      .hierarchy-search-row { display: flex; gap: 8px; }
      .hierarchy-search-row input { flex: 1; }
      .hierarchy { display: flex; flex-direction: column; gap: 10px; }
      .cat-card { padding: 0; overflow: hidden; }
      .cat-header {
        display: flex; align-items: center; gap: 12px; padding: 14px 18px; cursor: pointer;
        transition: background 150ms ease; font-weight: 600;
      }
      .cat-header:hover { background: rgba(255,255,255,0.04); }
      .cat-name { flex: 1; font-size: 14px; }
      .cat-body { padding: 0 18px 14px; border-top: 1px solid var(--border); display: flex; flex-direction: column; gap: 8px; padding-top: 10px; }
      .sec-card { background: rgba(255,255,255,0.02); border: 1px solid var(--border); border-radius: 10px; overflow: hidden; }
      .sec-header {
        display: flex; align-items: center; gap: 12px; padding: 10px 14px; cursor: pointer;
        transition: background 150ms ease; font-size: 13px;
      }
      .sec-header:hover { background: rgba(255,255,255,0.05); }
      .sec-desc { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .sec-standard { flex-shrink: 0; }
      .sec-body { padding: 8px 14px 12px; border-top: 1px solid var(--border); display: flex; flex-direction: column; gap: 6px; }
    `,
  ],
})
export class MaterialExplorerComponent {
  @ViewChild('comboboxCard') comboboxCardRef?: ElementRef<HTMLElement>;

  activeReleaseTab: ReleaseTab = 'current';
  activeView: ViewMode = 'flat';

  previousOptions: MaterialSectionOptionDto[] = [];
  currentOptions: MaterialSectionOptionDto[] = [];
  searchTerm = '';
  dropdownOpen = false;

  selectedCodes = new Set<string>();
  rows: EntrySummaryDto[] = [];
  loading = false;
  exporting = false;

  // ---- Hierarchy tab state ----
  hierarchy: CategoryGroup[] = [];
  hierarchySearchTerm = '';
  expandedCategories = new Set<number>();
  expandedSections = new Set<string>();

  // Closes the dropdown on ANY click outside the combobox card, regardless
  // of where else on the page it lands - more reliable than a separate
  // fixed-position backdrop div, which depends on z-index/stacking context
  // lining up correctly with every other element on the page.
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (this.dropdownOpen && this.comboboxCardRef && !this.comboboxCardRef.nativeElement.contains(event.target as Node)) {
      this.dropdownOpen = false;
    }
  }

  // Shared between both tabs deliberately - a supplier row expanded in one
  // view stays expanded if the user switches to the other, since it's the
  // exact same underlying entry either way.
  expandedIds = new Set<number>();
  detailCache = new Map<number, LotamEntryDetailDto>();
  changeTypesCache = new Map<number, string[]>();

  // Which expanded "section" of the detail view each ChangeType affects -
  // used to highlight exactly what changed on an Updated row, instead of
  // leaving the user to guess. Grounded in the real severity_rule seed
  // data's ChangeType set, not a guess.
  private static readonly CHANGE_TYPE_TO_SECTION: Record<string, string> = {
    StatusChanged: 'status',
    ModelAdded: 'model',
    ModelRemoved: 'model',
    RatingChanged: 'model',
    VoltageChanged: 'model',
    SpecificationChanged: 'model',
    ScopeChanged: 'model',
    ConditionChanged: 'remarks',
    RemarkChanged: 'remarks',
    OtherMaterialChange: 'remarks',
    AgentChanged: 'rep',
    CountryChanged: 'manufacturer',
  };

  constructor(
    private api: ApiService,
    protected selection: ReleaseSelectionService,
    private toast: ToastService,
  ) {
    effect(() => {
      const prev = this.selection.previousReleaseId();
      const curr = this.selection.currentReleaseId();
      if (prev !== null) this.api.getMaterialSectionsForRelease(prev).subscribe((o) => (this.previousOptions = o));
      if (curr !== null) this.api.getMaterialSectionsForRelease(curr).subscribe((o) => (this.currentOptions = o));
      this.resetViewState();
      this.loadRows();
    });
  }

  switchReleaseTab(tab: ReleaseTab) {
    this.activeReleaseTab = tab;
    this.resetViewState();
    this.loadRows();
  }

  private resetViewState() {
    this.searchTerm = '';
    this.dropdownOpen = false;
    this.selectedCodes.clear();
    this.expandedIds.clear();
    this.detailCache.clear();
    this.changeTypesCache.clear();
    this.hierarchySearchTerm = '';
    this.expandedCategories.clear();
    this.expandedSections.clear();
  }

  private activeReleaseId(): number | null {
    return this.activeReleaseTab === 'previous' ? this.selection.previousReleaseId() : this.selection.currentReleaseId();
  }

  selectedOptions(): MaterialSectionOptionDto[] {
    return this.activeReleaseTab === 'previous' ? this.previousOptions : this.currentOptions;
  }

  filteredOptions(): MaterialSectionOptionDto[] {
    const term = this.searchTerm.trim().toLowerCase();
    const options = this.selectedOptions();
    if (!term) return options.slice(0, 60);
    return options
      .filter((o) => o.label.toLowerCase().includes(term) || o.code.toLowerCase().includes(term))
      .slice(0, 60);
  }

  isChecked(o: MaterialSectionOptionDto): boolean {
    return this.selectedCodes.has(o.code);
  }

  toggleChecked(o: MaterialSectionOptionDto) {
    if (this.selectedCodes.has(o.code)) this.selectedCodes.delete(o.code);
    else this.selectedCodes.add(o.code);
    this.loadRows();
  }

  clearFilter() {
    this.selectedCodes.clear();
    this.loadRows();
  }

  private loadRows() {
    const releaseId = this.activeReleaseId();
    if (releaseId === null) {
      this.rows = [];
      this.hierarchy = [];
      return;
    }
    this.loading = true;
    const codes = this.selectedCodes.size > 0 ? [...this.selectedCodes] : undefined;
    const prev = this.selection.previousReleaseId() ?? undefined;
    const curr = this.selection.currentReleaseId() ?? undefined;
    this.api.getEntriesSummary(releaseId, codes, prev, curr).subscribe({
      next: (r) => {
        this.rows = r;
        this.loading = false;
        this.expandedIds.clear();
        this.expandedCategories.clear();
        this.expandedSections.clear();
        this.hierarchy = this.buildHierarchy(r);
      },
      error: () => { this.loading = false; },
    });
  }

  // Parses "1.10" -> [1, 10] so both the category grouping and the
  // within-category sort order match SEC's own numbering (1.1, 1.2, ...,
  // 1.10 - not alphabetical, which would put 1.10 before 1.2).
  private static parseSortKey(code: string): [number, number] {
    const parts = code.split('.', 2);
    const a = parseInt(parts[0], 10) || 0;
    const b = parts.length > 1 ? parseInt(parts[1], 10) || 0 : 0;
    return [a, b];
  }

  private buildHierarchy(rows: EntrySummaryDto[]): CategoryGroup[] {
    const categoryMap = new Map<number, CategoryGroup>();

    for (const r of rows) {
      const [groupKey] = MaterialExplorerComponent.parseSortKey(r.materialCode);
      let cat = categoryMap.get(groupKey);
      if (!cat) {
        cat = { groupKey, categoryName: r.categoryName, sections: [] };
        categoryMap.set(groupKey, cat);
      }

      let sec = cat.sections.find((s) => s.code === r.materialCode);
      if (!sec) {
        sec = { code: r.materialCode, description: r.materialDescription, applicableStandard: r.applicableStandard, rows: [] };
        cat.sections.push(sec);
      }
      sec.rows.push(r);
    }

    const categories = [...categoryMap.values()];
    categories.sort((a, b) => a.groupKey - b.groupKey);
    for (const cat of categories) {
      cat.sections.sort((a, b) => {
        const [, ab] = MaterialExplorerComponent.parseSortKey(a.code);
        const [, bb] = MaterialExplorerComponent.parseSortKey(b.code);
        return ab - bb;
      });
    }
    return categories;
  }

  // ---- Hierarchy tab: search (client-side, narrows + auto-expands) ----

  filteredHierarchy(): CategoryGroup[] {
    const term = this.hierarchySearchTerm.trim().toLowerCase();
    if (!term) return this.hierarchy;

    const result: CategoryGroup[] = [];
    for (const cat of this.hierarchy) {
      const catMatches = cat.categoryName.toLowerCase().includes(term) || String(cat.groupKey).includes(term);
      const matchingSections = cat.sections.filter((sec) => {
        if (catMatches) return true;
        if (sec.code.toLowerCase().includes(term)) return true;
        if (sec.description.toLowerCase().includes(term)) return true;
        if (sec.applicableStandard?.toLowerCase().includes(term)) return true;
        return sec.rows.some((r) => r.supplierName.toLowerCase().includes(term));
      });
      if (matchingSections.length > 0) {
        result.push({ ...cat, sections: matchingSections });
      }
    }
    return result;
  }

  onHierarchySearchChange() {
    // Auto-expand every branch that survives the filter, so the user sees
    // matches immediately instead of having to click into each one.
    const term = this.hierarchySearchTerm.trim();
    if (!term) return;
    for (const cat of this.filteredHierarchy()) {
      this.expandedCategories.add(cat.groupKey);
      for (const sec of cat.sections) this.expandedSections.add(sec.code);
    }
  }

  clearHierarchySearch() {
    this.hierarchySearchTerm = '';
  }

  isCategoryExpanded(cat: CategoryGroup): boolean {
    return this.expandedCategories.has(cat.groupKey);
  }

  toggleCategory(cat: CategoryGroup) {
    if (this.expandedCategories.has(cat.groupKey)) this.expandedCategories.delete(cat.groupKey);
    else this.expandedCategories.add(cat.groupKey);
  }

  isSectionExpanded(sec: SectionGroup): boolean {
    return this.expandedSections.has(sec.code);
  }

  toggleSection(sec: SectionGroup) {
    if (this.expandedSections.has(sec.code)) this.expandedSections.delete(sec.code);
    else this.expandedSections.add(sec.code);
  }

  // ---- Shared supplier-row expand/detail (both tabs) ----

  isExpanded(r: EntrySummaryDto): boolean {
    return this.expandedIds.has(r.entryId);
  }

  toggleExpand(r: EntrySummaryDto) {
    if (this.expandedIds.has(r.entryId)) {
      this.expandedIds.delete(r.entryId);
    } else {
      this.expandedIds.add(r.entryId);
      this.loadDetailIfNeeded(r);
    }
  }

  private loadDetailIfNeeded(r: EntrySummaryDto) {
    if (!this.detailCache.has(r.entryId)) {
      this.api.getLotamEntryDetail(r.entryId).subscribe((d) => this.detailCache.set(r.entryId, d));
    }
    if (r.badge === 'Updated' && !this.changeTypesCache.has(r.entryId)) {
      const prev = this.selection.previousReleaseId() ?? undefined;
      const curr = this.selection.currentReleaseId() ?? undefined;
      this.api.getEntryChangeTypes(r.entryId, prev, curr).subscribe((types) => this.changeTypesCache.set(r.entryId, types));
    }
  }

  detailFor(r: EntrySummaryDto): LotamEntryDetailDto | undefined {
    return this.detailCache.get(r.entryId);
  }

  // "section" is one of: status, model, remarks, rep, manufacturer - see
  // CHANGE_TYPE_TO_SECTION above.
  isSectionChanged(r: EntrySummaryDto, section: string): boolean {
    const types = this.changeTypesCache.get(r.entryId);
    if (!types) return false;
    return types.some((t) => MaterialExplorerComponent.CHANGE_TYPE_TO_SECTION[t] === section);
  }

  // Operates on whichever view is active PLUS the shared entry-level state,
  // so "Expand all"/"Collapse all" behaves predictably no matter which tab
  // the user expands from or switches to afterwards.
  expandAll() {
    for (const r of this.rows) {
      this.expandedIds.add(r.entryId);
      this.loadDetailIfNeeded(r);
    }
    for (const cat of this.hierarchy) {
      this.expandedCategories.add(cat.groupKey);
      for (const sec of cat.sections) this.expandedSections.add(sec.code);
    }
  }

  collapseAll() {
    this.expandedIds.clear();
    this.expandedCategories.clear();
    this.expandedSections.clear();
  }

  onExportDownload(event: ExportDownloadEvent): void {
    const releaseId = this.activeReleaseId();
    if (releaseId === null || this.selectedCodes.size === 0) return;

    this.exporting = true;
    const prev = this.selection.previousReleaseId() ?? undefined;
    const curr = this.selection.currentReleaseId() ?? undefined;
    this.api.exportMaterialsPdf(releaseId, [...this.selectedCodes], prev, curr, event.format).subscribe({
      next: (blob) => {
        const ext = event.format === 'html' ? 'html' : 'pdf';
        downloadBlob(blob, `LOTAM-Sentinel-Materials-${new Date().toISOString().slice(0, 10)}.${ext}`);
        this.exporting = false;
      },
      error: () => {
        this.exporting = false;
        this.toast.error('Could not generate the export. Please try again.');
      },
    });
  }

  onExportEmail(event: ExportEmailEvent): void {
    const releaseId = this.activeReleaseId();
    if (releaseId === null || this.selectedCodes.size === 0) return;

    this.exporting = true;
    const prev = this.selection.previousReleaseId() ?? undefined;
    const curr = this.selection.currentReleaseId() ?? undefined;
    this.api
      .emailMaterialsExport(releaseId, [...this.selectedCodes], event.format, event.email, prev, curr)
      .subscribe({
        // See ChangeExplorerComponent.onExportEmail for why both the
        // success and error branches check the actual ExportSendResultDto
        // outcome, not just whether the HTTP call itself succeeded.
        next: (result) => {
          this.exporting = false;
          if (result.sent) this.toast.success(result.message || `Emailed to ${event.email}.`);
          else this.toast.error(result.message || `Could not send the email to ${event.email}.`);
        },
        error: (err) => {
          this.exporting = false;
          this.toast.error(err?.error?.message || `Could not send the email to ${event.email}.`);
        },
      });
  }
}
