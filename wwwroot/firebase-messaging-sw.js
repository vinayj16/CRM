importScripts('https://www.gstatic.com/firebasejs/9.6.1/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/9.6.1/firebase-messaging-compat.js');

firebase.initializeApp({
  apiKey: "AIzaSyAvEHvheqC9sHavPGUILsbF4Byb3SKH1O4",
  authDomain: "realestate-crm-d8dae.firebaseapp.com",
  projectId: "realestate-crm-d8dae",
  storageBucket: "realestate-crm-d8dae.firebasestorage.app",
  messagingSenderId: "840903614246",
  appId: "1:840903614246:web:d30663f615726796eaabe7"
});

const messaging = firebase.messaging();

messaging.onBackgroundMessage(function(payload) {
  console.log('Received background message ', payload);
  
  var title = (payload.data && payload.data.title) || 'CRM Notification';
  var body = (payload.data && payload.data.body) || '';
  var link = (payload.data && payload.data.link) || '/';

  var notificationOptions = {
    body: body,
    icon: '/favicon.ico',
    badge: '/favicon.ico',
    data: { link: link }
  };

  self.registration.showNotification(title, notificationOptions);
});

self.addEventListener('notificationclick', function(event) {
  event.notification.close();
  var link = (event.notification.data && event.notification.data.link) || '/';
  event.waitUntil(clients.openWindow(link));
});