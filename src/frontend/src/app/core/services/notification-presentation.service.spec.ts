import { BehaviorSubject, Subject } from 'rxjs';
import { MessageService } from 'primeng/api';
import { AuthUser } from '../models/auth.models';
import { NotificationSummary } from '../models/realtime-notification.models';
import { AuthService } from './auth.service';
import { NotificationInboxService } from './notification-inbox.service';
import { BrowserNotificationService } from './browser-notification.service';
import {
  NotificationPresentationService,
  NotificationSoundPlayer
} from './notification-presentation.service';

describe('NotificationPresentationService', () => {
  let users: BehaviorSubject<AuthUser | null>;
  let live: Subject<NotificationSummary>;
  let messages: jasmine.SpyObj<MessageService>;
  let sound: jasmine.SpyObj<NotificationSoundPlayer>;
  let service: NotificationPresentationService;
  let browser: jasmine.SpyObj<BrowserNotificationService>;

  beforeEach(() => {
    window.localStorage.removeItem('plp.notifications.sound-enabled');
    users = new BehaviorSubject<AuthUser | null>(null);
    live = new Subject<NotificationSummary>();
    messages = jasmine.createSpyObj<MessageService>('MessageService', ['add']);
    sound = jasmine.createSpyObj<NotificationSoundPlayer>('NotificationSoundPlayer', ['initialize', 'play', 'teardown']);
    browser = jasmine.createSpyObj<BrowserNotificationService>('BrowserNotificationService', ['show']);
    service = new NotificationPresentationService(
      { currentUser$: users.asObservable() } as AuthService,
      { liveNotifications$: live.asObservable() } as NotificationInboxService,
      messages,
      sound,
      browser
    );
    service.initialize();
  });

  afterEach(() => service.ngOnDestroy());

  it('shows one toast and plays one sound for a unique live notification', () => {
    users.next(authenticatedUser());
    const value = notification();

    live.next(value);
    live.next(value);

    expect(messages.add).toHaveBeenCalledTimes(1);
    expect(sound.play).toHaveBeenCalledOnceWith('default', 0.2);
    expect(browser.show).toHaveBeenCalledTimes(1);
  });

  it('does not present before login or for a live item already marked read', () => {
    live.next(notification());
    users.next(authenticatedUser());
    const read = { ...notification(), id: '33333333-3333-3333-3333-333333333333', isRead: true };

    live.next(read);

    expect(messages.add).not.toHaveBeenCalled();
    expect(sound.play).not.toHaveBeenCalled();
  });

  it('supports disabling the future sound preference without changing transport or inbox state', () => {
    users.next(authenticatedUser());
    service.configureSoundPreferences({ enabled: false, volume: 0.8 });

    live.next(notification());

    expect(messages.add).toHaveBeenCalledTimes(1);
    expect(sound.play).not.toHaveBeenCalled();
  });

  it('cleans up on logout and permits the same notification id in a new user session', () => {
    users.next(authenticatedUser());
    live.next(notification());
    users.next(null);
    live.next({ ...notification(), title: 'Ignored after logout' });
    users.next(authenticatedUser('44444444-4444-4444-4444-444444444444'));
    live.next({ ...notification(), title: 'New user session' });

    expect(messages.add).toHaveBeenCalledTimes(2);
    expect(sound.play).toHaveBeenCalledTimes(2);
    expect(sound.teardown).toHaveBeenCalledTimes(4);
    expect(sound.initialize).toHaveBeenCalledTimes(2);
  });

  function authenticatedUser(id = '11111111-1111-1111-1111-111111111111'): AuthUser {
    return { id, fullName: 'User', email: 'user@test', roles: [], permissions: [] };
  }
});

describe('NotificationSoundPlayer', () => {
  it('installs one unlock pair, handles autoplay rejection, and unlocks on a later interaction', async () => {
    const audio = fakeAudioContext();
    const listeners = new Map<string, EventListener>();
    const document = fakeDocument(audio.context, listeners);
    const player = new NotificationSoundPlayer(document);
    audio.resume.and.returnValue(Promise.reject(new DOMException('Blocked', 'NotAllowedError')));

    player.initialize();
    player.initialize();
    expect(document.addEventListener).toHaveBeenCalledTimes(2);
    listeners.get('pointerdown')?.(new Event('pointerdown'));
    await settle();
    expect(document.removeEventListener).not.toHaveBeenCalled();

    audio.resume.and.callFake(() => {
      audio.setState('running');
      return Promise.resolve();
    });
    listeners.get('keydown')?.(new Event('keydown'));
    await settle();

    expect(document.removeEventListener).toHaveBeenCalledTimes(2);
    expect(audio.oscillator.start).toHaveBeenCalled();
    player.teardown();
  });

  it('plays a short generated tone only after unlock and handles close rejection silently', async () => {
    const audio = fakeAudioContext();
    audio.setState('running');
    const listeners = new Map<string, EventListener>();
    const document = fakeDocument(audio.context, listeners);
    const player = new NotificationSoundPlayer(document);

    player.initialize();
    listeners.get('pointerdown')?.(new Event('pointerdown'));
    await settle();
    audio.oscillator.start.calls.reset();
    audio.oscillator.stop.calls.reset();

    player.play('default', 0.4);

    expect(audio.oscillator.start).toHaveBeenCalledTimes(1);
    expect(audio.oscillator.stop).toHaveBeenCalledTimes(1);
    audio.close.and.returnValue(Promise.reject(new DOMException('Already closed', 'InvalidStateError')));
    player.teardown();
    await settle();
  });

  function fakeDocument(
    context: AudioContext,
    listeners: Map<string, EventListener>
  ): Document & {
    addEventListener: jasmine.Spy;
    removeEventListener: jasmine.Spy;
  } {
    const AudioContextConstructor = function(): AudioContext {
      return context;
    } as unknown as typeof AudioContext;
    const addEventListener = jasmine.createSpy('addEventListener').and.callFake(
      (type: string, listener: EventListenerOrEventListenerObject) => {
        listeners.set(type, listener as EventListener);
      }
    );
    const removeEventListener = jasmine.createSpy('removeEventListener').and.callFake(
      (type: string) => listeners.delete(type)
    );

    return {
      defaultView: { AudioContext: AudioContextConstructor },
      addEventListener,
      removeEventListener
    } as unknown as Document & {
      addEventListener: jasmine.Spy;
      removeEventListener: jasmine.Spy;
    };
  }

  function fakeAudioContext(): {
    context: AudioContext;
    oscillator: {
      start: jasmine.Spy;
      stop: jasmine.Spy;
    };
    resume: jasmine.Spy<() => Promise<void>>;
    close: jasmine.Spy<() => Promise<void>>;
    setState: (state: AudioContextState) => void;
  } {
    let state: AudioContextState = 'suspended';
    const oscillator = {
      type: 'sine' as OscillatorType,
      frequency: { setValueAtTime: jasmine.createSpy('setFrequency') },
      connect: jasmine.createSpy('connectOscillator'),
      start: jasmine.createSpy('start'),
      stop: jasmine.createSpy('stop')
    };
    const gain = {
      gain: {
        setValueAtTime: jasmine.createSpy('setGain'),
        exponentialRampToValueAtTime: jasmine.createSpy('rampGain')
      },
      connect: jasmine.createSpy('connectGain')
    };
    const resume = jasmine.createSpy('resume').and.callFake(() => {
      state = 'running';
      return Promise.resolve();
    });
    const close = jasmine.createSpy('close').and.callFake(() => {
      state = 'closed';
      return Promise.resolve();
    });
    const context = {
      get state(): AudioContextState { return state; },
      currentTime: 0,
      destination: {},
      createOscillator: jasmine.createSpy('createOscillator').and.returnValue(oscillator),
      createGain: jasmine.createSpy('createGain').and.returnValue(gain),
      resume,
      close
    } as unknown as AudioContext;

    return {
      context,
      oscillator,
      resume,
      close,
      setState: value => { state = value; }
    };
  }

  async function settle(): Promise<void> {
    await Promise.resolve();
    await Promise.resolve();
  }
});

function notification(): NotificationSummary {
  return {
    id: '22222222-2222-2222-2222-222222222222',
    title: 'Live notification',
    message: 'A new notification is available.',
    status: 'Unread',
    isRead: false,
    relatedEntityType: null,
    relatedEntityId: null,
    createdAtUtc: '2026-07-19T09:00:00Z',
    readAtUtc: null,
    isToastEnabled: true,
    isSoundEnabled: true,
    isBrowserEnabled: true
  };
}
