/**
 * The field API's shapes, mirrored from src/Truckroll.Api/Features/Field/FieldEndpoints.cs.
 *
 * Dates arrive as ISO strings (DateTimeOffset and DateOnly both serialize that way) and enums as
 * their names (the API registers JsonStringEnumConverter). Keep this file in step with the C#
 * records; the API is the source of truth.
 */

export type JobStatus = 'New' | 'Scheduled' | 'Dispatched' | 'InProgress' | 'Completed' | 'Cancelled';
export type JobType = 'Repair' | 'Maintenance' | 'Installation' | 'Estimate';
export type JobPriority = 'Emergency' | 'Urgent' | 'Normal';
export type NoteSource = 'Office' | 'Technician' | 'Mcp';

export interface FieldSignInRequest {
  email: string;
  password: string;
  device: string | null;
}

export interface FieldSignInResponse {
  /** A `trk_` personal token. Shown once; the app keeps it in secure storage. */
  token: string;
  expiresAt: string;
  displayName: string;
  technicianId: number;
}

export interface FieldDay {
  /** The business day, `YYYY-MM-DD`, in the company's time zone. */
  date: string;
  /** When the server answered: how fresh this copy is. */
  serverTime: string;
  technician: string;
  jobs: FieldJob[];
}

export interface FieldJob {
  id: number;
  number: string;
  status: JobStatus;
  type: JobType;
  priority: JobPriority;
  windowStart: string;
  windowEnd: string;
  estimatedMinutes: number;
  summary: string;
  resolution: string | null;
  customer: { name: string; phone: string };
  place: { street: string; city: string; accessNotes: string | null; latitude: number; longitude: number };
  equipment: FieldEquipment | null;
  /** The last five completed jobs at this address, newest first, whoever did them. */
  history: FieldVisit[];
  notes: FieldNote[];
}

export interface FieldEquipment {
  kind: string;
  brand: string;
  model: string;
  serialNumber: string;
  installedOn: string;
  warrantyExpiresOn: string | null;
}

export interface FieldVisit {
  number: string;
  date: string;
  type: JobType;
  summary: string;
  resolution: string | null;
  technician: string | null;
}

export interface FieldNote {
  author: string;
  source: NoteSource;
  body: string;
  createdAt: string;
}

export type FieldChangeKind = 'start' | 'complete' | 'note';

/** One thing the technician did, as the sync endpoint takes it. */
export interface FieldChangeRequest {
  /** A UUID made on the phone when the change was made. Sending it twice is safe. */
  id: string;
  jobId: number;
  kind: FieldChangeKind;
  /** When, by the phone's clock. The server clamps a clock that runs ahead. */
  madeAt: string;
  body: string | null;
  resolution: string | null;
  firstTimeFix: boolean | null;
}

export type FieldChangeOutcome = 'Applied' | 'AlreadyDone' | 'Rejected' | 'Invalid';

export interface FieldChangeResult {
  id: string;
  outcome: FieldChangeOutcome;
  /** What happened, in plain words for the technician. Null when it simply applied. */
  explanation: string | null;
}

export interface FieldSyncResponse {
  serverTime: string;
  results: FieldChangeResult[];
}
