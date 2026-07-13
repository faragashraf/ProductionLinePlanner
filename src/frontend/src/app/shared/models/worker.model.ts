export type WorkersPageState = 'جاهز' | 'متأخر' | 'غائب';

export interface WorkerPageItem {
  id?: string;
  code: string;
  fullName: string;
  state: WorkersPageState;
  email?: string;
  phone?: string;
  department?: string;
}
