import {
  Component, ElementRef, Input, OnChanges, OnDestroy, ViewChild, AfterViewInit, SimpleChanges,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';

export interface GlobeMarker {
  country: string;
  lat: number;
  lon: number;
  value: number;
  color: string;
  label: string;
}

// Official Three.js project asset - freely hosted, no API key, used in
// countless public examples for exactly this purpose. Loaded at runtime by
// the browser (which has real internet access), not fetched by anything in
// this build process. If your deployment environment blocks outbound
// requests to threejs.org, the fallback below kicks in automatically.
const EARTH_TEXTURE_URL = 'https://threejs.org/examples/textures/planets/earth_atmos_2048.jpg';

@Component({
  selector: 'app-globe',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="globe-wrap">
      <div #container class="globe-canvas"></div>
      <div class="tooltip" *ngIf="hovered" [style.left.px]="hoverX" [style.top.px]="hoverY">
        {{ hovered }}
      </div>
      <p class="hint">Drag to rotate, scroll or pinch to zoom.</p>
      <p class="hint warn" *ngIf="textureFailed">
        Couldn't load the Earth texture (likely no outbound internet access to threejs.org from
        here) - showing a plain sphere with markers instead. The data is unaffected.
      </p>
    </div>
  `,
  styles: [
    `
      .globe-wrap { position: relative; display: flex; flex-direction: column; align-items: center; gap: 8px; }
      .globe-canvas { width: 460px; height: 460px; max-width: 100%; cursor: grab; }
      .globe-canvas:active { cursor: grabbing; }
      .tooltip {
        position: absolute; transform: translate(-50%, -130%);
        background: rgba(19, 27, 48, 0.95); backdrop-filter: blur(10px);
        border: 1px solid var(--border-strong); border-radius: 8px;
        padding: 6px 10px; font-size: 12px; white-space: nowrap; pointer-events: none;
        box-shadow: var(--shadow); z-index: 5;
      }
      .hint { font-size: 11px; color: var(--text-dim); }
      .hint.warn { color: var(--hue-amber); }
    `,
  ],
})
export class GlobeComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('container', { static: true }) containerRef!: ElementRef<HTMLDivElement>;
  @Input() markers: GlobeMarker[] = [];

  hovered: string | null = null;
  hoverX = 0;
  hoverY = 0;
  textureFailed = false;

  private scene!: THREE.Scene;
  private camera!: THREE.PerspectiveCamera;
  private renderer!: THREE.WebGLRenderer;
  private controls!: OrbitControls;
  private globeGroup!: THREE.Group;
  private markerMeshes: THREE.Mesh[] = [];
  private raycaster = new THREE.Raycaster();
  private mouse = new THREE.Vector2();
  private frameId: number | null = null;
  private viewReady = false;

  ngAfterViewInit(): void {
    this.initScene();
    this.rebuildMarkers();
    this.animate();
    this.viewReady = true;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['markers'] && this.viewReady) {
      this.rebuildMarkers();
    }
  }

  ngOnDestroy(): void {
    if (this.frameId !== null) cancelAnimationFrame(this.frameId);
    this.controls?.dispose();
    this.markerMeshes.forEach((m) => {
      m.geometry.dispose();
      (m.material as THREE.Material).dispose();
    });
    this.renderer?.dispose();
  }

  private initScene(): void {
    const container = this.containerRef.nativeElement;
    const size = 460;

    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(45, 1, 0.1, 100);
    this.camera.position.z = 13;

    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    this.renderer.setSize(size, size);
    this.renderer.setClearColor(0x000000, 0);
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    container.appendChild(this.renderer.domElement);

    // Drag to rotate, scroll/pinch to zoom - no auto-rotate. Deliberately
    // simple: just the two interactions actually asked for.
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.08;
    this.controls.enableZoom = true;
    this.controls.enablePan = false;
    this.controls.minDistance = 7;
    this.controls.maxDistance = 26;

    this.globeGroup = new THREE.Group();
    this.scene.add(this.globeGroup);

    const sphereGeo = new THREE.SphereGeometry(5, 64, 64);
    const fallbackMaterial = new THREE.MeshPhongMaterial({ color: 0x14213d, shininess: 12 });
    const sphere = new THREE.Mesh(sphereGeo, fallbackMaterial);
    this.globeGroup.add(sphere);

    new THREE.TextureLoader().load(
      EARTH_TEXTURE_URL,
      (texture) => {
        texture.colorSpace = THREE.SRGBColorSpace;
        sphere.material = new THREE.MeshPhongMaterial({ map: texture, shininess: 12 });
      },
      undefined,
      (err) => {
        console.error('Globe: failed to load Earth texture, keeping plain sphere.', err);
        this.textureFailed = true;
      },
    );

    this.scene.add(new THREE.AmbientLight(0xffffff, 0.6));
    const point = new THREE.PointLight(0xffffff, 1.1);
    point.position.set(12, 8, 10);
    this.scene.add(point);

    container.addEventListener('mousemove', (e) => this.onMouseMove(e));
    container.addEventListener('mouseleave', () => (this.hovered = null));
  }

  private rebuildMarkers(): void {
    this.markerMeshes.forEach((m) => {
      this.globeGroup.remove(m);
      m.geometry.dispose();
      (m.material as THREE.Material).dispose();
    });
    this.markerMeshes = [];

    const maxValue = Math.max(1, ...this.markers.map((m) => m.value));

    for (const marker of this.markers) {
      const size = 0.06 + (marker.value / maxValue) * 0.18;
      const geo = new THREE.SphereGeometry(size, 12, 12);
      const mat = new THREE.MeshBasicMaterial({ color: new THREE.Color(marker.color) });
      const mesh = new THREE.Mesh(geo, mat);
      const pos = this.latLonToVector3(marker.lat, marker.lon, 5.1);
      mesh.position.copy(pos);
      mesh.userData['label'] = marker.label;
      this.globeGroup.add(mesh);
      this.markerMeshes.push(mesh);
    }
  }

  private latLonToVector3(lat: number, lon: number, radius: number): THREE.Vector3 {
    const phi = (90 - lat) * (Math.PI / 180);
    const theta = (lon + 180) * (Math.PI / 180);
    return new THREE.Vector3(
      -radius * Math.sin(phi) * Math.cos(theta),
      radius * Math.cos(phi),
      radius * Math.sin(phi) * Math.sin(theta),
    );
  }

  private onMouseMove(event: MouseEvent): void {
    const rect = this.renderer.domElement.getBoundingClientRect();
    this.mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    this.mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;

    this.raycaster.setFromCamera(this.mouse, this.camera);
    const hits = this.raycaster.intersectObjects(this.markerMeshes);
    if (hits.length > 0) {
      this.hovered = (hits[0].object.userData['label'] as string) ?? null;
      this.hoverX = event.clientX - rect.left;
      this.hoverY = event.clientY - rect.top;
    } else {
      this.hovered = null;
    }
  }

  private animate = (): void => {
    this.frameId = requestAnimationFrame(this.animate);
    this.controls.update(); // required every frame for damping
    this.renderer.render(this.scene, this.camera);
  };
}
