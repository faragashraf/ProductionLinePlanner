import { Component } from '@angular/core';

interface NotificationItem {
  title: string;
  message: string;
  severity: 'info' | 'warning' | 'critical';
  isRead: boolean;
}

@Component({
  selector: 'app-notifications-page',
  templateUrl: './notifications-page.component.html',
  styleUrls: ['./notifications-page.component.scss']
})
export class NotificationsPageComponent {
  notifications: NotificationItem[] = [
    { title: 'مرحلة بدون تغطية كافية', message: 'خط أحمر - مرحلة الخلط', severity: 'warning', isRead: false },
    { title: 'مزامنة الحضور مكتملة', message: 'تم تحديث حالة الحضور قبل دقيقة', severity: 'info', isRead: false },
    { title: 'تحديث جاهزية', message: 'الجاهزية العامة انخفضت إلى 82%', severity: 'critical', isRead: true },
  ];
}
