import { Component, ElementRef, HostListener, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, Observable } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import {
  AdminUserDto,
  NotificationSubscriptionDto,
  SubscribableOptionsDto,
  SubscriptionRecipientDto,
  SupplierOptionDto,
  UserDto,
} from '../../core/models';

type ProfileTab = 'info' | 'notifications' | 'admin';
// The kind picker only ever shows these three - 'MaterialSection' items are
// created behind the scenes when a specific subgroup (rather than a whole
// category) is checked in the material picker (see MaterialPickerOption).
type SubscriptionKind = 'MaterialCategory' | 'Technology' | 'Supplier';

// One row in the material picker's combined, searchable list - either a
// whole category (subscriptionType 'MaterialCategory', no code badge) or
// one specific material group/subgroup within a category (subscriptionType
// 'MaterialSection', code badge shown). Built once from
// SubscribableOptionsDto's materialCategories + materialSections.
interface MaterialPickerOption {
  subscriptionType: 'MaterialCategory' | 'MaterialSection';
  targetCode: string;
  targetLabel: string;
  displayCode: string;
}

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page">
      <h1>Profile</h1>
      <p class="subtitle">Your account info and what you get emailed about.</p>

      <div class="tabs">
        <button [class.active]="activeTab === 'info'" (click)="activeTab = 'info'">My Info</button>
        <button [class.active]="activeTab === 'notifications'" (click)="activeTab = 'notifications'">
          Notification Settings
        </button>
        <button *ngIf="auth.currentUser()?.isAdmin" [class.active]="activeTab === 'admin'" (click)="onAdminTab()">
          Admin
        </button>
      </div>

      <!-- ============ Tab 1: My Info ============ -->
      <div class="card" *ngIf="activeTab === 'info' && user">
        <div class="section-header">
          <h3>My Info</h3>
          <span class="header-actions">
            <span class="success" *ngIf="infoSaved && !infoEditMode">Saved.</span>
            <button *ngIf="!infoEditMode" class="linklike" (click)="startEditInfo()">Edit</button>
          </span>
        </div>

        <!-- Display mode - read only until "Edit" is clicked -->
        <div class="info-display" *ngIf="!infoEditMode">
          <div class="info-row"><span class="info-label">Email</span><span class="info-value">{{ user.email }}</span></div>
          <div class="info-row"><span class="info-label">Full name</span><span class="info-value">{{ user.fullName }}</span></div>
          <div class="info-row"><span class="info-label">Mobile</span><span class="info-value">{{ user.mobile || '—' }}</span></div>
          <div class="info-row"><span class="info-label">Address</span><span class="info-value">{{ user.address || '—' }}</span></div>
          <div class="info-row"><span class="info-label">Department</span><span class="info-value">{{ user.department || '—' }}</span></div>
        </div>

        <form *ngIf="infoEditMode" (ngSubmit)="saveInfo()">
          <label>Email <input type="email" [ngModel]="user.email" name="email" disabled /></label>
          <label>Full name <input type="text" [(ngModel)]="infoDraft.fullName" name="fullName" required /></label>
          <label>Mobile <input type="text" [(ngModel)]="infoDraft.mobile" name="mobile" /></label>
          <label>Address <input type="text" [(ngModel)]="infoDraft.address" name="address" /></label>
          <label>Department <input type="text" [(ngModel)]="infoDraft.department" name="department" /></label>
          <div class="actions">
            <button type="submit" class="primary" [disabled]="savingInfo">{{ savingInfo ? 'Saving...' : 'Save changes' }}</button>
            <button type="button" class="linklike" (click)="cancelEditInfo()">Cancel</button>
          </div>
        </form>

        <hr />

        <div class="section-header">
          <h3>Change password</h3>
          <span class="header-actions">
            <span class="success" *ngIf="passwordChanged && !passwordEditMode">Password changed.</span>
            <button *ngIf="!passwordEditMode" class="linklike" (click)="startEditPassword()">Edit</button>
          </span>
        </div>
        <p class="muted" *ngIf="!passwordEditMode">••••••••</p>

        <form *ngIf="passwordEditMode" (ngSubmit)="changePassword()">
          <label>Current password <input type="password" [(ngModel)]="currentPassword" name="currentPassword" required /></label>
          <label>New password <input type="password" [(ngModel)]="newPassword" name="newPassword" required /></label>
          <div class="actions">
            <button type="submit" class="primary" [disabled]="changingPassword">
              {{ changingPassword ? 'Updating...' : 'Change password' }}
            </button>
            <button type="button" class="linklike" (click)="cancelEditPassword()">Cancel</button>
            <span class="error" *ngIf="passwordError">{{ passwordError }}</span>
          </div>
        </form>
      </div>

      <!-- ============ Tab 2: Notification Settings ============ -->
      <div class="notifications-tab" *ngIf="activeTab === 'notifications'">
        <div class="card">
          <div class="panel-title-row">
            <div class="panel-title">Get emailed when something changes for...</div>
            <button
              class="primary"
              (click)="sendDigestNow()"
              [disabled]="subscriptions.length === 0 || sendingDigest"
              title="Builds and emails an up-to-the-minute digest for each of your subscriptions below, comparing the two most recent LOTAM releases"
            >
              {{ sendingDigest ? 'Queuing...' : 'Send digest now' }}
            </button>
          </div>
          <div class="actions" *ngIf="digestMessage || digestError" style="margin-bottom: 14px;">
            <span class="success" *ngIf="digestMessage">{{ digestMessage }}</span>
            <span class="error" *ngIf="digestError">{{ digestError }}</span>
          </div>
          <div class="add-row">
            <select [(ngModel)]="newKind" (ngModelChange)="onKindChange()">
              <option value="MaterialCategory">a material group</option>
              <option value="Technology">a technology area</option>
              <option value="Supplier">a specific supplier</option>
            </select>

            <button
              class="primary"
              (click)="addSubscription()"
              [disabled]="!canAdd() || adding"
            >
              {{ adding ? 'Adding...' : addButtonLabel() }}
            </button>
          </div>

          <!-- Material group picker: whole categories AND their specific
               material groups/subgroups, all in one searchable, multi-select
               list (mirrors Material Explorer's own filter combobox). -->
          <div class="picker-panel combobox-card" #materialComboboxCard *ngIf="newKind === 'MaterialCategory'">
            <label class="combo-label">Search materials - pick any number of whole groups and/or specific subgroups</label>
            <div class="combobox">
              <input
                type="text"
                placeholder="Search to narrow this list (category or description)..."
                [(ngModel)]="materialSearchTerm"
                (focus)="materialDropdownOpen = true"
                (input)="materialDropdownOpen = true"
              />
              <div class="combo-list" *ngIf="materialDropdownOpen && filteredMaterialOptions().length > 0">
                <label class="combo-item" *ngFor="let o of filteredMaterialOptions()">
                  <input
                    type="checkbox"
                    [checked]="isMaterialChecked(o)"
                    (change)="toggleMaterialChecked(o)"
                    (click)="$event.stopPropagation()"
                  />
                  <span class="combo-code" *ngIf="o.displayCode">{{ o.displayCode }}</span>
                  <span class="combo-badge" *ngIf="!o.displayCode">GROUP</span>
                  <span class="combo-label-text">{{ o.targetLabel }}</span>
                </label>
              </div>
            </div>
            <div class="filter-summary" *ngIf="selectedMaterialKeys.size > 0">
              <span class="muted">{{ selectedMaterialKeys.size }} selected</span>
              <button (click)="clearMaterialSelection()">Clear</button>
            </div>
          </div>

          <!-- Technology picker: short, fixed list - plain multi-select checkboxes. -->
          <div class="picker-panel" *ngIf="newKind === 'Technology'">
            <label class="combo-label">Pick any number of technology areas</label>
            <div class="checkbox-grid">
              <label class="checkbox-chip" *ngFor="let t of options?.technologies">
                <input
                  type="checkbox"
                  [checked]="selectedTechnologyCodes.has(t.code)"
                  (change)="toggleTechnology(t.code)"
                />
                {{ t.name }}
              </label>
            </div>
          </div>

          <!-- Supplier picker: existing search, now toggles into a multi-select set. -->
          <div class="picker-panel" *ngIf="newKind === 'Supplier'">
            <label class="combo-label">Search suppliers by name - pick any number</label>
            <input
              type="text"
              placeholder="Search supplier by name..."
              [(ngModel)]="supplierSearchTerm"
              (ngModelChange)="onSupplierSearch()"
            />
            <div class="supplier-results" *ngIf="supplierResults.length > 0">
              <button
                *ngFor="let s of supplierResults"
                class="chip"
                [class.active]="isSupplierSelected(s)"
                (click)="toggleSupplier(s)"
              >
                {{ s.canonicalName }} <span class="muted" *ngIf="s.canonicalCountry">({{ s.canonicalCountry }})</span>
              </button>
            </div>
            <div class="filter-summary" *ngIf="selectedSuppliers.length > 0">
              <span class="muted">{{ selectedSuppliers.length }} selected: {{ selectedSupplierNames() }}</span>
              <button (click)="clearSupplierSelection()">Clear</button>
            </div>
          </div>
        </div>

        <div class="card" *ngIf="subscriptions.length > 0">
          <div class="panel-title">Subscribed to {{ subscriptions.length }}</div>
          <div class="sub-list">
            <div class="sub-item" *ngFor="let s of subscriptions">
              <div class="sub-row">
                <span class="sub-type">{{ kindLabel(s.subscriptionType) }}</span>
                <span class="sub-label">{{ s.targetLabel }}</span>
                <button class="linklike" (click)="toggleRecipients(s)">
                  {{ s.recipients.length > 0 ? (s.recipients.length + ' recipient' + (s.recipients.length === 1 ? '' : 's')) : 'Add recipient' }}
                </button>
                <button class="linklike" (click)="removeSubscription(s)">Remove</button>
              </div>

              <div class="recipients-panel" *ngIf="expandedSubscriptionId === s.id">
                <div class="recipient-chip" *ngFor="let r of s.recipients">
                  {{ r.name || r.email }} <span class="muted" *ngIf="r.name">({{ r.email }})</span>
                  <button class="chip-x" (click)="removeRecipient(s, r)" title="Remove recipient">✕</button>
                </div>
                <div class="add-row">
                  <input type="email" placeholder="Recipient email" [(ngModel)]="newRecipientEmail" name="newRecipientEmail" />
                  <input type="text" placeholder="Name (optional)" [(ngModel)]="newRecipientName" name="newRecipientName" />
                  <button class="primary" (click)="addRecipient(s)" [disabled]="!newRecipientEmail.trim() || addingRecipient">
                    {{ addingRecipient ? 'Adding...' : 'Add' }}
                  </button>
                </div>
                <p class="muted recipients-note">
                  They'll receive the same colorful report email whenever you click "Send digest now" above (manual, for
                  now) - or automatically once the scheduled digest job is live (see README "Known limitations").
                </p>
              </div>
            </div>
          </div>
        </div>
        <div class="card muted" *ngIf="subscriptions.length === 0">
          Not subscribed to anything yet - add one above to get emailed when it changes.
        </div>
      </div>

      <!-- ============ Tab 3: Admin (admins only) ============ -->
      <div *ngIf="activeTab === 'admin'">
        <div class="card">
          <div class="panel-title">Invite a user</div>
          <div class="add-row">
            <input type="email" placeholder="Email address" [(ngModel)]="inviteEmail" name="inviteEmail" />
            <input type="text" placeholder="Full name" [(ngModel)]="inviteFullName" name="inviteFullName" />
            <input type="text" placeholder="Department (optional)" [(ngModel)]="inviteDepartment" name="inviteDepartment" />
            <label class="checkbox-label">
              <input type="checkbox" [(ngModel)]="inviteIsAdmin" name="inviteIsAdmin" />
              Make admin
            </label>
            <button class="primary" (click)="inviteUser()" [disabled]="!canInvite() || inviting">
              {{ inviting ? 'Inviting...' : 'Send invite' }}
            </button>
          </div>
          <div class="actions" style="margin-top: 10px;">
            <span class="error" *ngIf="inviteError">{{ inviteError }}</span>
            <span class="success" *ngIf="inviteSuccessMessage">{{ inviteSuccessMessage }}</span>
          </div>
          <p class="muted" style="margin-top: 4px;">
            A temporary password is generated and emailed to them (see Email / SMTP setup in the README if this isn't configured yet -
            the account is still created, and you'll be shown the temporary password here instead).
          </p>
        </div>

        <div class="card" style="margin-top: 16px;" *ngIf="adminUsers.length > 0">
          <div class="panel-title">Users ({{ adminUsers.length }})</div>
          <table class="data-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Email</th>
                <th>Department</th>
                <th>Role</th>
                <th>Status</th>
                <th>Last login</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let u of adminUsers">
                <td>{{ u.fullName }}</td>
                <td>{{ u.email }}</td>
                <td>{{ u.department || '—' }}</td>
                <td>{{ u.isAdmin ? 'Admin' : 'User' }}</td>
                <td>{{ u.isActive ? 'Active' : 'Deactivated' }}</td>
                <td>{{ u.lastLoginAt ? (u.lastLoginAt | date: 'medium') : 'Never' }}</td>
                <td>
                  <button
                    class="linklike"
                    *ngIf="u.id !== auth.currentUser()?.id"
                    (click)="toggleActive(u)"
                  >
                    {{ u.isActive ? 'Deactivate' : 'Reactivate' }}
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .subtitle { color: var(--text-dim); margin-bottom: 16px; }
      .tabs { display: flex; gap: 8px; margin-bottom: 16px; }
      .tabs button.active {
        background: linear-gradient(135deg, var(--accent), var(--accent-2));
        border: none; color: white;
      }
      form { display: flex; flex-direction: column; gap: 14px; max-width: 420px; }
      label { display: flex; flex-direction: column; gap: 6px; font-size: 12.5px; color: var(--text-dim); }
      input, select { padding: 9px 11px; font-size: 13.5px; }
      .actions { display: flex; align-items: center; gap: 12px; }
      .success { color: var(--hue-emerald, #22c55e); font-size: 12.5px; }
      .error { color: var(--hue-rose, #f04438); font-size: 12.5px; }
      hr { border: none; border-top: 1px solid var(--border); margin: 24px 0; }
      h3 { margin: 0 0 4px; }
      .panel-title { font-size: 11px; text-transform: uppercase; letter-spacing: 0.06em; color: var(--text-dim); margin-bottom: 12px; }
      /* Gap between the "Get emailed..." card and the "Subscribed to" list
         below it, and between that list and its own rows - .card has no
         margin of its own (it's a shared surface style used everywhere), so
         stacked cards on this tab need it added explicitly here. */
      .notifications-tab { display: flex; flex-direction: column; gap: 16px; }
      .panel-title-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 12px; }
      .panel-title-row .panel-title { margin-bottom: 0; }
      .add-row { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin-bottom: 14px; }
      .add-row:last-child { margin-bottom: 0; }
      .add-row select, .add-row input { min-width: 220px; }
      .supplier-results { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; margin-bottom: 4px; }
      .chip {
        border: 1px solid var(--border-strong); background: transparent; color: var(--text-dim);
        border-radius: 999px; padding: 6px 14px; font-size: 12.5px; cursor: pointer;
      }
      .chip.active { background: var(--accent); color: white; border-color: transparent; }

      /* One of these renders below the add-row, inside the same outer
         .card - a plain border-top separator instead of nesting another
         full .card (which would double up the glass/blur/shadow look). */
      .picker-panel { padding-top: 14px; border-top: 1px solid var(--border); }

      /* Material group/subgroup combobox - same pattern as Material
         Explorer's own filter combobox (search input + checkbox list),
         reused here so both pages feel consistent. */
      .combobox-card { position: relative; overflow: visible; z-index: 30; margin-top: 0; }
      .combo-label { display: block; font-size: 11px; color: var(--text-dim); text-transform: uppercase; letter-spacing: 0.06em; margin-bottom: 8px; }
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
      .combo-badge {
        font-size: 10px; font-weight: 700; letter-spacing: 0.04em; color: var(--text-dim);
        background: rgba(255,255,255,0.06); border-radius: 5px; padding: 2px 6px; flex-shrink: 0;
      }
      .combo-label-text { flex: 1; color: var(--text); }
      .filter-summary { display: flex; align-items: center; gap: 10px; margin-top: 10px; }

      /* Technology multi-select - a short, fixed list, so plain wrapping
         checkbox chips rather than a searchable combobox. */
      .checkbox-grid { display: flex; flex-wrap: wrap; gap: 8px 18px; }
      .checkbox-chip { display: flex; align-items: center; gap: 8px; font-size: 13px; cursor: pointer; }
      .checkbox-chip input[type='checkbox'] { accent-color: var(--accent); width: 15px; height: 15px; }
      .sub-list { display: flex; flex-direction: column; gap: 10px; }
      .sub-item { padding-bottom: 6px; border-bottom: 1px solid var(--border); }
      .sub-item:last-child { border-bottom: none; padding-bottom: 0; }
      .sub-row { display: flex; align-items: center; gap: 12px; padding: 12px 4px; }
      .sub-type {
        font-size: 10.5px; text-transform: uppercase; letter-spacing: 0.04em; color: var(--text-dim);
        background: rgba(255,255,255,0.05); padding: 3px 8px; border-radius: 6px; flex-shrink: 0;
      }
      .sub-label { flex: 1; font-size: 13px; }
      .muted { color: var(--text-dim); font-size: 13px; }
      .checkbox-label { display: flex; align-items: center; gap: 6px; font-size: 12.5px; color: var(--text-dim); }
      .checkbox-label input { width: auto; padding: 0; }
      .data-table th, .data-table td { text-align: left; }

      /* Edit-mode gating (Tab 1) */
      .section-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 14px; }
      .section-header h3 { margin: 0; }
      .header-actions { display: flex; align-items: center; gap: 10px; }
      .info-display { display: flex; flex-direction: column; gap: 12px; max-width: 420px; margin-bottom: 4px; }
      .info-row { display: flex; flex-direction: column; gap: 3px; padding-bottom: 10px; border-bottom: 1px solid var(--border); }
      .info-row:last-child { border-bottom: none; padding-bottom: 0; }
      .info-label { font-size: 11px; text-transform: uppercase; letter-spacing: 0.04em; color: var(--text-dim); }
      .info-value { font-size: 13.5px; }

      /* Per-subscription recipients (Tab 2) */
      .recipients-panel {
        margin: 0 4px 14px; padding: 14px 16px; background: rgba(255,255,255,0.03);
        border: 1px solid var(--border); border-radius: var(--radius-sm);
      }
      .recipient-chip {
        display: inline-flex; align-items: center; gap: 6px; background: rgba(255,255,255,0.05);
        border: 1px solid var(--border-strong); border-radius: 999px; padding: 5px 10px 5px 12px;
        font-size: 12px; margin: 0 8px 8px 0;
      }
      .chip-x {
        background: none; border: none; color: var(--text-dim); cursor: pointer; font-size: 11px;
        padding: 0; line-height: 1;
      }
      .chip-x:hover { color: var(--hue-rose, #f04438); }
      .recipients-note { margin: 10px 0 0; line-height: 1.5; }
    `,
  ],
})
export class ProfileComponent implements OnInit {
  activeTab: ProfileTab = 'info';
  user: UserDto | null = null;

  infoEditMode = false;
  infoDraft: { fullName: string; mobile: string | null; address: string | null; department: string | null } = {
    fullName: '', mobile: null, address: null, department: null,
  };
  savingInfo = false;
  infoSaved = false;

  passwordEditMode = false;
  currentPassword = '';
  newPassword = '';
  changingPassword = false;
  passwordError = '';
  passwordChanged = false;

  @ViewChild('materialComboboxCard') materialComboboxCardRef?: ElementRef<HTMLElement>;

  options: SubscribableOptionsDto | null = null;
  subscriptions: NotificationSubscriptionDto[] = [];
  newKind: SubscriptionKind = 'MaterialCategory';
  adding = false;

  // Material group/subgroup picker (multi-select combobox - see
  // MaterialPickerOption). materialPickerOptions is built once from
  // `options` (categories + material sections combined); selectedMaterialKeys
  // holds `${subscriptionType}:${targetCode}` for each checked row.
  materialPickerOptions: MaterialPickerOption[] = [];
  materialSearchTerm = '';
  materialDropdownOpen = false;
  selectedMaterialKeys = new Set<string>();

  // Technology picker (multi-select checkboxes - short fixed list).
  selectedTechnologyCodes = new Set<string>();

  // Supplier picker (existing search, now a multi-select set of chips).
  supplierSearchTerm = '';
  supplierResults: SupplierOptionDto[] = [];
  selectedSuppliers: SupplierOptionDto[] = [];

  expandedSubscriptionId: number | null = null;
  newRecipientEmail = '';
  newRecipientName = '';
  addingRecipient = false;

  sendingDigest = false;
  digestMessage = '';
  digestError = '';

  adminUsers: AdminUserDto[] = [];
  inviteEmail = '';
  inviteFullName = '';
  inviteDepartment = '';
  inviteIsAdmin = false;
  inviting = false;
  inviteError = '';
  inviteSuccessMessage = '';

  constructor(
    private api: ApiService,
    protected auth: AuthService,
  ) {}

  ngOnInit(): void {
    this.api.getProfile().subscribe((u) => (this.user = u));
    this.api.getSubscribableOptions().subscribe((o) => {
      this.options = o;
      this.materialPickerOptions = [
        ...o.materialCategories.map((c) => ({
          subscriptionType: 'MaterialCategory' as const, targetCode: c, targetLabel: c, displayCode: '',
        })),
        ...o.materialSections.map((s) => ({
          subscriptionType: 'MaterialSection' as const, targetCode: s.code, targetLabel: s.label, displayCode: s.code,
        })),
      ];
    });
    this.loadSubscriptions();
  }

  // Closes the material combobox on any click outside it - same approach
  // Material Explorer's own combobox uses, for the same reason (more
  // reliable than a backdrop div fighting z-index/stacking elsewhere on
  // the page).
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (
      this.materialDropdownOpen &&
      this.materialComboboxCardRef &&
      !this.materialComboboxCardRef.nativeElement.contains(event.target as Node)
    ) {
      this.materialDropdownOpen = false;
    }
  }

  startEditInfo(): void {
    if (!this.user) return;
    this.infoDraft = {
      fullName: this.user.fullName, mobile: this.user.mobile, address: this.user.address, department: this.user.department,
    };
    this.infoSaved = false;
    this.infoEditMode = true;
  }

  cancelEditInfo(): void {
    this.infoEditMode = false;
  }

  saveInfo(): void {
    if (!this.user) return;
    this.savingInfo = true;
    this.infoSaved = false;
    this.api
      .updateProfile(this.infoDraft.fullName, this.infoDraft.mobile, this.infoDraft.address, this.infoDraft.department)
      .subscribe({
        next: (u) => {
          this.savingInfo = false;
          this.infoSaved = true;
          this.user = u;
          this.auth.updateCachedUser(u);
          this.infoEditMode = false;
        },
        error: () => { this.savingInfo = false; },
      });
  }

  startEditPassword(): void {
    this.currentPassword = '';
    this.newPassword = '';
    this.passwordError = '';
    this.passwordChanged = false;
    this.passwordEditMode = true;
  }

  cancelEditPassword(): void {
    this.passwordEditMode = false;
    this.currentPassword = '';
    this.newPassword = '';
  }

  changePassword(): void {
    this.changingPassword = true;
    this.passwordError = '';
    this.passwordChanged = false;
    this.auth.changePassword(this.currentPassword, this.newPassword).subscribe({
      next: () => {
        this.changingPassword = false;
        this.passwordChanged = true;
        this.currentPassword = '';
        this.newPassword = '';
        this.passwordEditMode = false;
      },
      error: (err) => {
        this.changingPassword = false;
        this.passwordError = err?.error?.message || 'Could not change password.';
      },
    });
  }

  private loadSubscriptions(): void {
    this.api.getNotificationSubscriptions().subscribe((s) => (this.subscriptions = s));
  }

  onKindChange(): void {
    this.materialSearchTerm = '';
    this.materialDropdownOpen = false;
    this.selectedMaterialKeys.clear();
    this.selectedTechnologyCodes.clear();
    this.supplierSearchTerm = '';
    this.supplierResults = [];
    this.selectedSuppliers = [];
  }

  // ---- Material group/subgroup picker ----

  private materialKey(o: MaterialPickerOption): string {
    return `${o.subscriptionType}:${o.targetCode}`;
  }

  filteredMaterialOptions(): MaterialPickerOption[] {
    const term = this.materialSearchTerm.trim().toLowerCase();
    if (!term) return this.materialPickerOptions;
    return this.materialPickerOptions.filter(
      (o) => o.targetLabel.toLowerCase().includes(term) || o.displayCode.toLowerCase().includes(term),
    );
  }

  isMaterialChecked(o: MaterialPickerOption): boolean {
    return this.selectedMaterialKeys.has(this.materialKey(o));
  }

  toggleMaterialChecked(o: MaterialPickerOption): void {
    const key = this.materialKey(o);
    if (this.selectedMaterialKeys.has(key)) this.selectedMaterialKeys.delete(key);
    else this.selectedMaterialKeys.add(key);
  }

  clearMaterialSelection(): void {
    this.selectedMaterialKeys.clear();
  }

  // ---- Technology picker ----

  toggleTechnology(code: string): void {
    if (this.selectedTechnologyCodes.has(code)) this.selectedTechnologyCodes.delete(code);
    else this.selectedTechnologyCodes.add(code);
  }

  // ---- Supplier picker ----

  onSupplierSearch(): void {
    const term = this.supplierSearchTerm.trim();
    if (term.length < 2) {
      this.supplierResults = [];
      return;
    }
    this.api.searchSuppliers(term).subscribe((r) => (this.supplierResults = r));
  }

  isSupplierSelected(s: SupplierOptionDto): boolean {
    return this.selectedSuppliers.some((x) => x.id === s.id);
  }

  toggleSupplier(s: SupplierOptionDto): void {
    if (this.isSupplierSelected(s)) this.selectedSuppliers = this.selectedSuppliers.filter((x) => x.id !== s.id);
    else this.selectedSuppliers = [...this.selectedSuppliers, s];
  }

  clearSupplierSelection(): void {
    this.selectedSuppliers = [];
  }

  selectedSupplierNames(): string {
    return this.selectedSuppliers.map((s) => s.canonicalName).join(', ');
  }

  // ---- Add (any number of items in one click, across all three kinds) ----

  private selectedCount(): number {
    if (this.newKind === 'MaterialCategory') return this.selectedMaterialKeys.size;
    if (this.newKind === 'Technology') return this.selectedTechnologyCodes.size;
    return this.selectedSuppliers.length;
  }

  addButtonLabel(): string {
    const n = this.selectedCount();
    return n > 1 ? `Add (${n})` : 'Add';
  }

  canAdd(): boolean {
    return this.selectedCount() > 0;
  }

  addSubscription(): void {
    if (!this.canAdd()) return;
    this.adding = true;

    const calls: Observable<NotificationSubscriptionDto>[] = [];

    if (this.newKind === 'MaterialCategory') {
      for (const o of this.materialPickerOptions) {
        if (this.selectedMaterialKeys.has(this.materialKey(o)))
          calls.push(this.api.addNotificationSubscription(o.subscriptionType, o.targetCode, o.targetLabel));
      }
    } else if (this.newKind === 'Technology') {
      for (const t of this.options?.technologies ?? []) {
        if (this.selectedTechnologyCodes.has(t.code))
          calls.push(this.api.addNotificationSubscription('Technology', t.code, t.name));
      }
    } else {
      for (const s of this.selectedSuppliers)
        calls.push(this.api.addNotificationSubscription('Supplier', String(s.id), s.canonicalName));
    }

    // forkJoin fires every add in parallel and reports back once they've
    // all finished - simpler than chaining, and fine here since it's at
    // most a few dozen quick inserts, not the report-building/emailing
    // work SubscriptionDigestQueue exists to serialize.
    forkJoin(calls).subscribe({
      next: () => {
        this.adding = false;
        this.onKindChange();
        this.loadSubscriptions();
      },
      error: () => { this.adding = false; },
    });
  }

  removeSubscription(s: NotificationSubscriptionDto): void {
    this.api.removeNotificationSubscription(s.id).subscribe(() => this.loadSubscriptions());
  }

  toggleRecipients(s: NotificationSubscriptionDto): void {
    this.expandedSubscriptionId = this.expandedSubscriptionId === s.id ? null : s.id;
    this.newRecipientEmail = '';
    this.newRecipientName = '';
  }

  addRecipient(s: NotificationSubscriptionDto): void {
    const email = this.newRecipientEmail.trim();
    if (!email) return;
    this.addingRecipient = true;
    this.api.addSubscriptionRecipient(s.id, email, this.newRecipientName.trim() || null).subscribe({
      next: () => {
        this.addingRecipient = false;
        this.newRecipientEmail = '';
        this.newRecipientName = '';
        this.loadSubscriptions();
      },
      error: () => { this.addingRecipient = false; },
    });
  }

  removeRecipient(s: NotificationSubscriptionDto, r: SubscriptionRecipientDto): void {
    this.api.removeSubscriptionRecipient(s.id, r.id).subscribe(() => this.loadSubscriptions());
  }

  // Queues one digest per subscription below (see ProfileController.SendDigest /
  // SubscriptionDigestWorker on the backend) - the server sends them one at a
  // time in the background and this just confirms how many were queued, since
  // waiting for every email to actually go out could take a while for an
  // account with many subscriptions.
  sendDigestNow(): void {
    if (this.subscriptions.length === 0 || this.sendingDigest) return;
    this.sendingDigest = true;
    this.digestMessage = '';
    this.digestError = '';
    this.api.sendSubscriptionDigest().subscribe({
      next: (result) => {
        this.sendingDigest = false;
        this.digestMessage =
          `Queued ${result.queued} subscription digest${result.queued === 1 ? '' : 's'} - ` +
          `sending in the background now (subscriptions with no changes since the previous release are skipped).`;
      },
      error: (err) => {
        this.sendingDigest = false;
        this.digestError = err?.error?.message || 'Could not queue the digest.';
      },
    });
  }

  kindLabel(kind: string): string {
    switch (kind) {
      case 'MaterialCategory': return 'Material Group';
      case 'MaterialSection': return 'Material';
      case 'Technology': return 'Technology';
      case 'Supplier': return 'Supplier';
      default: return kind;
    }
  }

  onAdminTab(): void {
    this.activeTab = 'admin';
    this.loadAdminUsers();
  }

  private loadAdminUsers(): void {
    this.api.getAdminUsers().subscribe((u) => (this.adminUsers = u));
  }

  canInvite(): boolean {
    return this.inviteEmail.trim().length > 0 && this.inviteFullName.trim().length > 0;
  }

  inviteUser(): void {
    if (!this.canInvite()) return;
    this.inviting = true;
    this.inviteError = '';
    this.inviteSuccessMessage = '';

    this.api
      .inviteUser(this.inviteEmail.trim(), this.inviteFullName.trim(), this.inviteDepartment.trim() || null, this.inviteIsAdmin)
      .subscribe({
        next: (result) => {
          this.inviting = false;
          this.inviteSuccessMessage = result.emailSent
            ? `Invite sent to ${result.user.email}.`
            : `User created, but the invite email could not be sent (SMTP isn't configured yet) - share their temporary password another way.`;
          this.inviteEmail = '';
          this.inviteFullName = '';
          this.inviteDepartment = '';
          this.inviteIsAdmin = false;
          this.loadAdminUsers();
        },
        error: (err) => {
          this.inviting = false;
          this.inviteError = err?.error?.message || 'Could not invite this user.';
        },
      });
  }

  toggleActive(u: AdminUserDto): void {
    this.api.setUserActive(u.id, !u.isActive).subscribe(() => this.loadAdminUsers());
  }
}
