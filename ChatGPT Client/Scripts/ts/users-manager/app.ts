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
                    data: "profilePicture", orderable: false, width: '25%',
                    render: (data, type, row) => {
                        return `<img src="data:image/jpg;base64,${row.profilePicture}" class="rounded-circle shadow-1-strong" width=50 height=50> 
                                <br />
                                <span><strong>Name: </strong> ${row.firstName} ${row.lastName}</span>
                                <br />
                                <span><strong>Id: </strong> ${row.id}</span>`;
                    }
                },
                { data: "birthDate", orderable: true, width: '5%' },
                {
                    data: "userName", orderable: true, width: '5%',
                    render: (data, type, row) => {
                        return `<span>${row.userName}</span>
                                <br />
                                <span><strong>Normalized: </strong> ${row.normalizedUserName}</span>`;
                    }
                },
                {
                    data: "email", orderable: true, width: '10%',
                    render: (data, type, row) => {
                        return `<span>${row.email}</span><span class="float-end ${row.emailConfirmed ? 'text-primary' : 'text-warning'}"><i class="${row.emailConfirmed ? 'fas fa-check-circle' : 'fas fa-exclamation-circle'}"></i></span>
                                <br />
                                <span><strong>Normalized: </strong> ${row.normalizedEmail}</span>`;
                    }
                },
                { data: "passwordHash", orderable: true, width: '10%' },
                { data: "securityStamp", orderable: true, width: '10%' },
                { data: "concurrencyStamp", orderable: true, width: '10%' },
                {
                    data: "phoneNumber", orderable: true, width: '5%',
                    render: (data, type, row) => {
                        return `<span>${row.phoneNumber}</span><span class="float-end ${row.phoneNumberConfirmed ? 'text-primary' : 'text-warning'}"><i class="${row.phoneNumberConfirmed ? 'fas fa-check-circle' : 'fas fa-exclamation-circle'}"></i></span>`
                    }
                },
                {
                    data: "twoFactorEnabled", orderable: true, width: '5%',
                    render: (data) => {
                        return `<input class="form-check-input" type="checkbox"${data ? 'checked' : ''} />`;
                    }
                },
                { data: "lockoutEnd", orderable: true, width: '5%' },
                {
                    data: "lockoutEnabled", orderable: true, width: '5%',
                    render: (data) => {
                        return `<input class="form-check-input" type="checkbox" ${data ? 'checked' : ''} />`;
                    }
                },
                { data: "accessFailedCount", orderable: true, width: '5%' }
            ],
            order: [[1, 'asc']],
            fixedHeader: { header: true },
            responsive: {
                details: false
            },
            autoWidth: true,
            searching: true,
            fixedColumns: true,
            scrollX: true,
            paging: true
        });
    }
}