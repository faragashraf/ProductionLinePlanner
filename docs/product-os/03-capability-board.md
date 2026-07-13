# Capability Index

هذا الفهرس هو نقطة الدخول إلى الحالة التشغيلية لكل Capability. ملف capability هو السجل التفصيلي الملزم للنطاق، حالة المراجعة، وHardening backlog؛ لا يُستخدم هذا الفهرس لتكرار تلك التفاصيل.

| Capability | Current Status | Current Branch | سجل Capability |
| --- | --- | --- | --- |
| IAM Foundation | Merged | غير منطبق — capability مدموجة | [iam-foundation](capabilities/iam-foundation.md) |
| Employee & Department Master Data | Planned | غير محدد | [employee-master-data](capabilities/employee-master-data.md) |
| Worker Compensation | Planned | غير محدد | [worker-compensation](capabilities/worker-compensation.md) |
| Production Stage Catalog | Planned | غير محدد | [production-stage-catalog](capabilities/production-stage-catalog.md) |
| Product Models | Planned | غير محدد | [product-models](capabilities/product-models.md) |
| Production Cost Recording V1 | Planned | غير محدد | [production-cost-recording](capabilities/production-cost-recording.md) |
| Recommendation Engine | Deferred | غير محدد | [recommendation-engine](capabilities/recommendation-engine.md) |

## قواعد الفهرس

- عند بدء Capability، يُحدد `Current Branch` وReview Status في سجلها قبل التنفيذ.
- عند كل Checkpoint أو Merge أو قرار Hardening، يُحدّث السجل أولاً ثم الفهرس إذا تغيرت الحالة.
- الحالة المعتمدة هي إحدى: `Planned`، `In Progress`، `In Review`، `Checkpointed`، `Merged`، `Hardening`، `Deferred`، `Released`.
