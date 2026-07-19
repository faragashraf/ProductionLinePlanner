import { StageRendererComponent } from './stage-renderer.component';
import { MainStageLayout } from '../../../../shared/models/factory-visualization.model';

describe('StageRendererComponent', () => {
  const mainStage: MainStageLayout = {
    id: 'main-stage',
    name: 'الخياطة',
    type: 'main-stage',
    subStages: [
      { id: 'full', name: 'حضور كامل', type: 'sub-stage', workers: [], workersCurrent: 2, presentAssignedWorkers: 2, attendanceSummaryAvailable: true, attendanceStatus: 'FullyPresent' },
      { id: 'partial', name: 'حضور جزئي', type: 'sub-stage', workers: [], workersCurrent: 3, presentAssignedWorkers: 2, attendanceSummaryAvailable: true, attendanceStatus: 'PartiallyPresent' },
      { id: 'absent', name: 'كلهم غائبون', type: 'sub-stage', workers: [], workersCurrent: 1, presentAssignedWorkers: 0, attendanceSummaryAvailable: true, attendanceStatus: 'AllAbsent' },
      { id: 'sync', name: 'تحتاج مزامنة', type: 'sub-stage', workers: [], workersCurrent: 1, attendanceSummaryAvailable: true, attendanceStatus: 'NeedsSync' },
      { id: 'empty', name: 'دون تسكين', type: 'sub-stage', workers: [], workersCurrent: 0, attendanceSummaryAvailable: true, attendanceStatus: 'NoAssignments' },
      { id: 'forbidden', name: 'غير متاح', type: 'sub-stage', workers: [], workersCurrent: 1, attendanceSummaryAvailable: false, attendanceStatus: 'NotAuthorized' }
    ]
  };

  function createComponent(): StageRendererComponent {
    const component = new StageRendererComponent();
    component.mainStage = mainStage;
    return component;
  }

  it('filters only stages with available attendance and at least one absence', () => {
    const component = createComponent();
    component.setAttendanceFilter('has-absence');

    expect(component.filteredSubStages.map((stage) => stage.id)).toEqual(['partial', 'absent']);
  });

  it('filters every explicit attendance state without reloading the map', () => {
    const component = createComponent();

    component.setAttendanceFilter('fully-present');
    expect(component.filteredSubStages.map((stage) => stage.id)).toEqual(['full']);

    component.setAttendanceFilter('partially-present');
    expect(component.filteredSubStages.map((stage) => stage.id)).toEqual(['partial']);

    component.setAttendanceFilter('all-absent');
    expect(component.filteredSubStages.map((stage) => stage.id)).toEqual(['absent']);

    component.setAttendanceFilter('needs-sync');
    expect(component.filteredSubStages.map((stage) => stage.id)).toEqual(['sync']);

    component.setAttendanceFilter('no-assignments');
    expect(component.filteredSubStages.map((stage) => stage.id)).toEqual(['empty']);
  });

  it('restores all stages from the local filter state', () => {
    const component = createComponent();
    component.setAttendanceFilter('all-absent');
    component.clearAttendanceFilter();

    expect(component.filteredSubStages).toHaveSize(6);
  });
});
