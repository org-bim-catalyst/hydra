import 'datatables.net-bs5';
import 'datatables.net-fixedheader-bs5';
import 'datatables.net-responsive-bs5';
import DataTable from 'datatables.net-fixedcolumns-bs5';

//https://ralzohairi.medium.com/audio-recording-in-javascript-96eed45b75ee
//https://orangeable.com/javascript/equalizer-web-audio-api
//https://github.com/orangeable/javascript-equalizer/blob/master/js/main.js
//https://orangeable.com/javascript/equalizer-web-audio-api
//https://github.com/orangeable/javascript-equalizer
//https://openai.com/api/
//https://platform.openai.com/examples
//https://blog.teamtreehouse.com/getting-started-speech-synthesis-api#:~:text=To%20use%20a%20voice%2C%20set,speechSynthesis.
//https://developer.mozilla.org/en-US/docs/Web/API/SpeechSynthesis
//https://ourcodeworld.com/articles/read/405/how-to-convert-pdf-to-text-extract-text-from-pdf-with-javascript
//https://medium.com/@david.richards.tech/ai-audio-conversations-with-openai-whisper-3c730a9c7123


export default class app {

    constructor() {

        this.initUi();
    }

    private initUi() {

        //https://datatables.net/forums/discussion/53257/multiple-data-in-single-cell
        //https://datatables.net/examples/api/multi_filter_select.html
        let table = new DataTable('#myTable', {
            ajax: {
                url: '/UsersManager/api/users',
                dataSrc: ''
            }, columns: [
                {
                    data: "profilePicture", orderable: false, width: '13%',
                    "render": (data, type, row) => {
                        return `<img src="data:image/jpg;base64,${row.profilePicture}" class="rounded-circle shadow-1-strong" width=50 height=50> <span>${row.firstName} ${row.lastName}</span>`;
                    }
                },
                //{ data: "firstName", orderable: true, width: '5%' },
                //{ data: "lastName", orderable: true, width: '5%' },
                { data: "birthDate", orderable: true, width: '5%' },
                { data: "id", orderable: true, width: '11.5%' },
                { data: "userName", orderable: true, width: '5%' },
                { data: "normalizedUserName", orderable: true, width: '5%' },
                { data: "email", orderable: true, width: '5%' },
                { data: "normalizedEmail", orderable: true, width: '5%' },
                {
                    data: "emailConfirmed", orderable: true, width: '5%', "render": (data) => {
                        return `<input class="form-check-input" type="checkbox" ${data ? 'checked' : ''} />`;
                    }
                },
                { data: "passwordHash", orderable: true, width: '5%' },
                { data: "securityStamp", orderable: true, width: '5%' },
                { data: "concurrencyStamp", orderable: true, width: '5%' },
                { data: "phoneNumber", orderable: true, width: '5%' },
                {
                    data: "phoneNumberConfirmed", orderable: true, width: '5%', "render": (data) => {
                        return `<input class="form-check-input" type="checkbox" ${data ? 'checked' : ''} />`;
                    }
                },
                {
                    data: "twoFactorEnabled", orderable: true, width: '5%', "render": (data) => {
                        return `<input class="form-check-input" type="checkbox"${data ? 'checked' : ''} />`;
                    }
                },
                { data: "lockoutEnd", orderable: true, width: '5%' },
                {
                    data: "lockoutEnabled", orderable: true, width: '5%', "render": (data) => {
                        return `<input class="form-check-input" type="checkbox" ${data ? 'checked':''} />`;
                    }
                },
                { data: "accessFailedCount", orderable: true, width: '5%' }
            ],
            order: [[5, 'asc']],
            fixedHeader: { header:true},
            responsive: {
                details: false
            },
            searching: true,
            fixedColumns: true,
            scrollX: true,
            paging: true
        });
    }
}