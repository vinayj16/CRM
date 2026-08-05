// firebase-messaging.js
import { initializeApp } from "https://www.gstatic.com/firebasejs/9.6.1/firebase-app.js";
import { getMessaging, getToken } from "https://www.gstatic.com/firebasejs/9.6.1/firebase-messaging.js";

const firebaseConfig = {
  apiKey: "AIzaSyAvEHvheqC9sHavPGUILsbF4Byb3SKH1O4",
  authDomain: "realestate-crm-d8dae.firebaseapp.com",
  projectId: "realestate-crm-d8dae",
  storageBucket: "realestate-crm-d8dae.firebasestorage.app",
  messagingSenderId: "840903614246",
  appId: "1:840903614246:web:d30663f615726796eaabe7"
};

const app = initializeApp(firebaseConfig);
const messaging = getMessaging(app);

const vapidKey = 'BJtdDW_n_5JtS8EUIGVfoeAAp3R6iW-rXjq2SNjz2ndU6SJJ937pIYCZGoy6CQYmebEt30d8vSPBE-jodaCgZx0';
getToken(messaging, { vapidKey })
  .then((currentToken) => {
    if (currentToken) {
      console.log('Device token:', currentToken);
      console.log('Device token: ' + currentToken);
    } else {
      console.log('No registration token available. Request permission to generate one.');
    }
  })
  .catch((err) => {
    console.log('An error occurred while retrieving token. ', err);
  });
