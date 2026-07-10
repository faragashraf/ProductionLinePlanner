export type WorkersPageState = 'جاهز' | 'متأخر' | 'غائب';

export interface WorkerPageItem {
  code: string;
  fullName: string;
  state: WorkersPageState;
}
