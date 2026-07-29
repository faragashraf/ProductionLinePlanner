export const PERMISSIONS = {
  workers: {
    view: 'workers.view',
    manage: 'workers.manage',
    export: 'workers.export'
  },
  departments: {
    view: 'departments.view',
    manage: 'departments.manage'
  },
  attendance: {
    view: 'attendance.view',
    sync: 'attendance.sync'
  },
  factoryStructure: {
    view: 'factory-structure.view',
    manage: 'factory-structure.manage'
  },
  assignments: {
    view: 'assignments.view',
    manage: 'assignments.manage'
  },
  compensation: {
    view: 'compensation.view',
    manage: 'compensation.manage',
    import: 'compensation.import',
    export: 'compensation.export'
  },
  stages: {
    view: 'stages.view',
    manage: 'stages.manage',
    delete: 'stages.delete',
    import: 'stages.import',
    export: 'stages.export'
  },
  models: {
    view: 'models.view',
    manage: 'models.manage'
  },
  production: {
    view: 'production.view',
    record: 'production.record',
    approve: 'production.approve'
  },
  reports: {
    productionView: 'reports.production.view',
    financialView: 'reports.financial.view'
  },
  users: {
    view: 'users.view',
    manage: 'users.manage'
  },
  roles: {
    view: 'roles.view',
    manage: 'roles.manage'
  },
  permissions: {
    assign: 'permissions.assign'
  },
  audit: {
    view: 'audit.view'
  },
  notifications: {
    policiesManage: 'notifications.policies.manage'
  }
} as const;

export type PermissionValue = (typeof PERMISSIONS)[keyof typeof PERMISSIONS][keyof (typeof PERMISSIONS)[keyof typeof PERMISSIONS]];
