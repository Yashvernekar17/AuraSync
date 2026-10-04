import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, throwError } from 'rxjs';
import { timeout } from 'rxjs/operators';
import {
  DisplaySource,
  AudioDevice,
  Profile,
  ProfileSettings,
  LedDeviceSettings,
  SyncConfigurationRequest,
  SyncStatus,
} from './api.models';

const API = 'http://127.0.0.1:5078/api/v1';
const FRAME_SOCKET = 'ws://127.0.0.1:5078/api/v1/sync/frames';
const REQUEST_TIMEOUT_MS = 12000;

/** Typed client for the loopback REST control API and binary frame socket. */
@Injectable({ providedIn: 'root' })
export class LedSyncApiService {
  private readonly http = inject(HttpClient);

  getProfiles(): Observable<Profile[]> {
    return this.withRequestTimeout(this.http.get<Profile[]>(`${API}/profiles`));
  }

  createProfile(name: string, settings: ProfileSettings): Observable<Profile> {
    return this.withRequestTimeout(this.http.post<Profile>(`${API}/profiles`, { name, settings }));
  }

  updateProfile(id: string, name: string, settings: ProfileSettings): Observable<Profile> {
    return this.withRequestTimeout(this.http.put<Profile>(`${API}/profiles/${id}`, { name, settings }));
  }

  deleteProfile(id: string): Observable<void> {
    return this.withRequestTimeout(this.http.delete<void>(`${API}/profiles/${id}`));
  }

  activateProfile(id: string): Observable<SyncStatus> {
    return this.withRequestTimeout(this.http.post<SyncStatus>(`${API}/profiles/${id}/activate`, {}));
  }

  getDisplays(): Observable<DisplaySource[]> {
    return this.withRequestTimeout(this.http.get<DisplaySource[]>(`${API}/displays`));
  }

  getSerialPorts(): Observable<string[]> {
    return this.withRequestTimeout(this.http.get<string[]>(`${API}/serial/ports`));
  }

  getAudioDevices(): Observable<AudioDevice[]> {
    return this.withRequestTimeout(this.http.get<AudioDevice[]>(`${API}/audio/devices`));
  }

  setDisplay(displayId: string | null): Observable<SyncStatus> {
    return this.withRequestTimeout(this.http.put<SyncStatus>(`${API}/sync/source`, { displayId }));
  }

  getStatus(): Observable<SyncStatus> {
    return this.withRequestTimeout(this.http.get<SyncStatus>(`${API}/sync/status`));
  }

  setConfiguration(configuration: SyncConfigurationRequest & { ledDevice: LedDeviceSettings }): Observable<SyncStatus> {
    return this.withRequestTimeout(this.http.put<SyncStatus>(`${API}/sync/config`, configuration));
  }

  frameSocketUrl(): string {
    return FRAME_SOCKET;
  }

  start(configuration: SyncConfigurationRequest & { ledDevice: LedDeviceSettings }): Observable<SyncStatus> {
    return this.withRequestTimeout(this.http.post<SyncStatus>(`${API}/sync/start`, configuration));
  }

  stop(): Observable<SyncStatus> {
    return this.withRequestTimeout(this.http.post<SyncStatus>(`${API}/sync/stop`, {}));
  }

  private withRequestTimeout<T>(request: Observable<T>): Observable<T> {
    // Bound each HTTP call so a stopped local service cannot leave UI actions pending indefinitely.
    return request.pipe(timeout({
      first: REQUEST_TIMEOUT_MS,
      with: () => throwError(() => new Error('The local AuraSync service did not respond in time.')),
    }));
  }

  errorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const detail = error.error?.detail;
      return typeof detail === 'string' ? detail : `${error.status} ${error.statusText}`;
    }
    return error instanceof Error ? error.message : 'An unexpected error occurred.';
  }
}
