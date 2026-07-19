import { SubStageCardComponent } from './sub-stage-card.component';

describe('SubStageCardComponent', () => {
  it('labels a fully staffed structural assignment as complete assignment rather than operational readiness', () => {
    const component = new SubStageCardComponent();
    component.workersCurrent = 1;
    component.workersRequired = 1;
    component.workerRequirementDefined = true;

    expect(component.staffingStatusLabel).toBe('التسكين مكتمل');
  });

  it('labels an undefined requirement without producing a readiness claim', () => {
    const component = new SubStageCardComponent();
    component.workersCurrent = 1;
    component.workerRequirementDefined = false;

    expect(component.staffingStatusLabel).toBe('الاحتياج غير محدد');
  });

  it('shows attendance independently when a structurally assigned worker is absent', () => {
    const component = new SubStageCardComponent();
    component.workersCurrent = 1;
    component.workersRequired = 1;
    component.presentAssignedWorkers = 0;
    component.attendanceStatus = 'AllAbsent';

    expect(component.staffingStatusLabel).toBe('التسكين مكتمل');
    expect(component.attendanceSummary).toBe('0 من 1 - جميع المسكنين غائبون');
    expect(component.attendancePercentage).toBe(0);
  });

  it('rounds attendance percentage from the assigned workers only', () => {
    const component = new SubStageCardComponent();
    component.workersCurrent = 3;
    component.presentAssignedWorkers = 2;
    component.attendanceStatus = 'PartiallyPresent';

    expect(component.attendancePercentage).toBe(67);
  });

  it('does not present an attendance percentage when attendance needs sync or there are no assignments', () => {
    const component = new SubStageCardComponent();
    component.workersCurrent = 1;
    component.attendanceStatus = 'NeedsSync';

    expect(component.attendancePercentage).toBeNull();

    component.workersCurrent = 0;
    component.attendanceStatus = 'NoAssignments';

    expect(component.attendancePercentage).toBeNull();
  });
});
