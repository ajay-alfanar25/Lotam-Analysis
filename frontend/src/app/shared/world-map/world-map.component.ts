import {
  Component, ElementRef, EventEmitter, Input, OnChanges, OnDestroy, Output, ViewChild,
  AfterViewInit, SimpleChanges,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import * as L from 'leaflet';

export interface MapMarker {
  country: string;
  lat: number;
  lon: number;
  value: number;
  color: string;
  label: string;
}

// Esri World Imagery - free satellite/aerial basemap, no API key required
// for this kind of light, internal usage (a standard, widely-used choice
// for exactly this purpose - countless free Leaflet demos use this same
// URL). Genuine satellite tiles, matching the reference visual style,
// rather than the flat cartoon-style streets look of standard OSM tiles.
const TILE_URL = 'https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}';
const TILE_ATTRIBUTION = 'Tiles &copy; Esri &mdash; Source: Esri, Maxar, Earthstar Geographics, and the GIS User Community';

@Component({
  selector: 'app-world-map',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="map-wrap">
      <div #container class="map-canvas"></div>
      <p class="hint">Drag to pan, scroll or pinch to zoom. Click a country label or a numbered pin for its suppliers.</p>
    </div>
  `,
  styles: [
    `
      :host { display: flex; flex: 1; min-height: 0; height: 100%; }
      .map-wrap { display: flex; flex-direction: column; gap: 8px; height: 100%; }
      .map-canvas {
        width: 100%; flex: 1; min-height: 0; border-radius: var(--radius-sm);
        border: 1px solid var(--border); overflow: hidden;
      }
      .hint { font-size: 11px; color: var(--text-dim); flex-shrink: 0; }

      /* Permanent dark-pill country labels, matching the reference style. */
      ::ng-deep .region-label {
        background: rgba(15, 23, 42, 0.92) !important;
        color: #fff !important;
        border: none !important;
        border-radius: 7px !important;
        padding: 4px 11px !important;
        font-weight: 600;
        font-size: 12px;
        box-shadow: 0 2px 8px rgba(0,0,0,0.35);
      }
      ::ng-deep .region-label::before { display: none !important; }

      /* Numbered circular badge markers, matching the reference style. */
      ::ng-deep .count-badge {
        display: flex; align-items: center; justify-content: center;
        border-radius: 50%; color: #fff; font-weight: 700; font-size: 13px;
        border: 2px solid rgba(255,255,255,0.55);
        box-shadow: 0 2px 10px rgba(0,0,0,0.45);
        cursor: pointer;
      }
    `,
  ],
})
export class WorldMapComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('container', { static: true }) containerRef!: ElementRef<HTMLDivElement>;
  @Input() markers: MapMarker[] = [];
  @Output() countrySelected = new EventEmitter<string>();

  private map: L.Map | null = null;
  private markerLayer: L.LayerGroup | null = null;
  private viewReady = false;

  ngAfterViewInit(): void {
    this.map = L.map(this.containerRef.nativeElement, {
      center: [24, 30],
      zoom: 3,
      minZoom: 1,
      maxZoom: 12,
      worldCopyJump: true,
    });

    L.tileLayer(TILE_URL, {
      attribution: TILE_ATTRIBUTION,
      maxZoom: 19,
    }).addTo(this.map);

    this.markerLayer = L.layerGroup().addTo(this.map);
    this.rebuildMarkers();
    this.viewReady = true;

    setTimeout(() => this.map?.invalidateSize(), 0);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['markers'] && this.viewReady) {
      this.rebuildMarkers();
    }
  }

  ngOnDestroy(): void {
    this.map?.remove();
  }

  private rebuildMarkers(): void {
    if (!this.markerLayer) return;
    this.markerLayer.clearLayers();

    for (const marker of this.markers) {
      const size = 34;
      const icon = L.divIcon({
        className: 'count-badge',
        html: `<div class="count-badge" style="width:${size}px;height:${size}px;background:${marker.color};">${marker.value}</div>`,
        iconSize: [size, size],
        iconAnchor: [size / 2, size / 2],
      });

      L.marker([marker.lat, marker.lon], { icon })
        .bindTooltip(marker.country, {
          permanent: true,
          direction: 'bottom',
          offset: [0, size / 2 + 6],
          className: 'region-label',
        })
        .on('click', () => this.countrySelected.emit(marker.country))
        .addTo(this.markerLayer);
    }
  }
}
