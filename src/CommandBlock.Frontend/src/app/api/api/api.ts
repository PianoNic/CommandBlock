export * from './activity.service';
import { ActivityService } from './activity.service';
export * from './app.service';
import { AppService } from './app.service';
export * from './server.service';
import { ServerService } from './server.service';
export const APIS = [ActivityService, AppService, ServerService];
